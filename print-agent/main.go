package main

import (
	"bytes"
	"context"
	"encoding/json"
	"errors"
	"fmt"
	"io"
	"log"
	"net"
	"net/http"
	"net/url"
	"os"
	"os/signal"
	"path/filepath"
	"regexp"
	"strings"
	"time"
)

type printer struct {
	Address string `json:"address"`
	Cut bool `json:"cut"`
}

type config struct {
	Server string `json:"server"`
	Printers map[string]printer `json:"printers"`
	StateDir string `json:"stateDir"`
	TokenFile string `json:"tokenFile"`
}

type job struct {
	ID string `json:"id"`
	Lease string `json:"lease"`
	Printer string `json:"printer"`
	Text string `json:"text"`
}

type result struct {
	Status string `json:"status"`
	Reason string `json:"reason,omitempty"`
}

type agent struct {
	cfg config
	token string
	client *http.Client
	dial func(context.Context, string, string) (net.Conn, error)
}

var jobID = regexp.MustCompile(`^[A-Za-z0-9_-]{1,100}$`)

func validateConfig(c config) error {
	u, err := url.Parse(c.Server)
	if err != nil || u.Scheme != "https" || u.Hostname() == "" ||
		u.User != nil || u.RawQuery != "" || u.Fragment != "" || (u.Path != "" && u.Path != "/") {
		return errors.New("server must be an HTTPS origin")
	}
	if c.StateDir == "" || c.TokenFile == "" || len(c.Printers) == 0 {
		return errors.New("stateDir, tokenFile and printers are required")
	}
	for id, p := range c.Printers {
		if !jobID.MatchString(id) {
			return errors.New("invalid printer identifier")
		}
		host, port, err := net.SplitHostPort(p.Address)
		if err != nil || host == "" || port == "" {
			return errors.New("printer address must be host:port")
		}
	}
	return nil
}

func (a *agent) post(ctx context.Context, path string, body any, output any) (int, error) {
	payload, err := json.Marshal(body)
	if err != nil { return 0, err }
	req, err := http.NewRequestWithContext(ctx, http.MethodPost,
		strings.TrimRight(a.cfg.Server, "/")+path, bytes.NewReader(payload))
	if err != nil { return 0, err }
	req.Header.Set("Authorization", "Bearer "+a.token)
	req.Header.Set("Content-Type", "application/json")
	resp, err := a.client.Do(req)
	if err != nil { return 0, errors.New("server connection failed") }
	defer resp.Body.Close()
	if resp.StatusCode == http.StatusNoContent { return resp.StatusCode, nil }
	if resp.StatusCode != http.StatusOK {
		return resp.StatusCode, fmt.Errorf("server returned HTTP %d", resp.StatusCode)
	}
	if output != nil {
		// Reject oversized responses, including a valid JSON prefix followed by junk.
		b, err := io.ReadAll(io.LimitReader(resp.Body, 65537))
		if err != nil || len(b) > 65536 { return 0, errors.New("invalid response size") }
		if err := json.Unmarshal(b, output); err != nil { return 0, errors.New("invalid server response") }
	}
	return resp.StatusCode, nil
}

func (a *agent) statePath(id string) string { return filepath.Join(a.cfg.StateDir, id+".json") }

func (a *agent) readResult(id string) (result, error) {
	b, err := os.ReadFile(a.statePath(id))
	if err != nil { return result{}, err }
	var r result
	if err := json.Unmarshal(b, &r); err != nil { return result{}, err }
	switch r.Status {
	case "sending": return result{Status:"uncertain", Reason:"interrupted_previous_attempt"}, nil
	case "sent", "uncertain": return r, nil
	default: return result{}, errors.New("invalid journal state")
	}
}

// Write the intent before sending any printer bytes. A crash after that point is
// deliberately ambiguous: do not silently print a second copy. The server must
// keep such jobs assigned to this agent and offer explicit operator reprint.
func (a *agent) saveResult(id string, r result) error {
	b, err := json.Marshal(r)
	if err != nil { return err }
	f, err := os.CreateTemp(a.cfg.StateDir, ".journal-")
	if err != nil { return err }
	defer os.Remove(f.Name())
	if _, err = f.Write(b); err == nil { err = f.Sync() }
	closeErr := f.Close()
	if err != nil { return err }
	if closeErr != nil { return closeErr }
	if err := os.Rename(f.Name(), a.statePath(id)); err != nil { return err }
	return syncDirectory(a.cfg.StateDir)
}

func receipt(text string, cut bool) []byte {
	// Only the agent may generate ESC/POS control bytes. The API supplies text,
	// never raw printer commands. ASCII is portable across printer code pages.
	var b bytes.Buffer
	b.Write([]byte{0x1b, 0x40})
	for _, r := range strings.ReplaceAll(text, "\r\n", "\n") {
		switch {
		case r == '\n': b.WriteByte('\n')
		case r >= 32 && r <= 126: b.WriteRune(r)
		default:
			const accented = "ÀÁÂÃÄÅàáâãäåÈÉÊËèéêëÌÍÎÏìíîïÒÓÔÕÖòóôõöÙÚÛÜùúûüÇçÑñ"
			const plain =    "AAAAAAaaaaaaEEEEeeeeIIIIiiiiOOOOOoooooUUUUuuuuCcNn"
			i := strings.IndexRune(accented, r)
			if i >= 0 { b.WriteByte(plain[len([]rune(accented[:i]))]) } else if r >= 32 && r != 127 { b.WriteByte('?') }
		}
	}
	b.WriteString("\n\n\n")
	if cut { b.Write([]byte{0x1d, 0x56, 0x00}) }
	return b.Bytes()
}

func (a *agent) process(ctx context.Context, j job) result {
	if r, err := a.readResult(j.ID); err == nil { return r
	} else if !os.IsNotExist(err) {
		return result{Status:"uncertain", Reason:"journal_unreadable"}
	}
	p, ok := a.cfg.Printers[j.Printer]
	if !ok { return result{Status:"retry", Reason:"unknown_printer"} }
	if j.Text == "" || len(j.Text) > 16384 {
		return result{Status:"uncertain", Reason:"invalid_receipt"}
	}
	dialCtx, cancel := context.WithTimeout(ctx, 5*time.Second)
	defer cancel()
	conn, err := a.dial(dialCtx, "tcp", p.Address)
	if err != nil { return result{Status:"retry", Reason:"printer_unreachable"} }
	defer conn.Close()
	if err := conn.SetWriteDeadline(time.Now().Add(10*time.Second)); err != nil {
		return result{Status:"retry", Reason:"printer_deadline_failed"}
	}
	if err := a.saveResult(j.ID, result{Status:"sending"}); err != nil {
		return result{Status:"retry", Reason:"journal_write_failed"}
	}
	data := receipt(j.Text, p.Cut)
	n, err := conn.Write(data)
	r := result{Status:"sent"}
	if err != nil || n != len(data) {
		r = result{Status:"uncertain", Reason:"printer_write_interrupted"}
	}
	// TCP success means accepted by the network stack, not physical printing.
	if err := a.saveResult(j.ID, r); err != nil {
		return result{Status:"uncertain", Reason:"journal_commit_failed"}
	}
	return r
}

func (a *agent) tick(ctx context.Context) error {
	var j job
	status, err := a.post(ctx, "/api/print-agent/v1/claim", struct{}{}, &j)
	if err != nil { return err }
	if status == http.StatusNoContent { return nil }
	if !jobID.MatchString(j.ID) || j.Lease == "" || len(j.Lease) > 256 {
		return errors.New("invalid job identity")
	}
	r := a.process(ctx, j)
	_, err = a.post(ctx, "/api/print-agent/v1/result", struct {
		ID string `json:"id"`
		Lease string `json:"lease"`
		result
	}{j.ID, j.Lease, r}, nil)
	if err == nil { log.Printf("job %s: %s", j.ID, r.Status) }
	return err
}

func run(ctx context.Context, a *agent) {
	delay := 2*time.Second
	for ctx.Err() == nil {
		if err := a.tick(ctx); err != nil {
			if ctx.Err() != nil { return }
			log.Printf("poll: %v", err)
			delay *= 2
			if delay > 60*time.Second { delay = 60*time.Second }
		} else { delay = 2*time.Second }
		timer := time.NewTimer(delay)
		select {
		case <-ctx.Done(): timer.Stop(); return
		case <-timer.C:
		}
	}
}

func main() {
	if len(os.Args) != 2 { log.Fatal("usage: openresto-print-agent CONFIG.json") }
	b, err := os.ReadFile(os.Args[1])
	if err != nil { log.Fatal("could not read configuration") }
	var c config
	dec := json.NewDecoder(bytes.NewReader(b))
	dec.DisallowUnknownFields()
	if err := dec.Decode(&c); err != nil { log.Fatal("invalid configuration JSON") }
	if err := validateConfig(c); err != nil { log.Fatal(err) }
	b, err = os.ReadFile(c.TokenFile)
	if err != nil { log.Fatal("could not read agent token") }
	token := strings.TrimSpace(string(b))
	if token == "" || strings.ContainsAny(token, "\r\n") { log.Fatal("invalid agent token") }
	if err := os.MkdirAll(c.StateDir, 0700); err != nil { log.Fatal("cannot create state directory") }
	unlock, err := lockState(c.StateDir)
	if err != nil { log.Fatal("state directory is already in use or unavailable") }
	defer unlock()
	dialer := &net.Dialer{}
	a := &agent{cfg:c, token:token, dial:dialer.DialContext,
		client:&http.Client{Timeout:15*time.Second, CheckRedirect:func(*http.Request, []*http.Request) error { return http.ErrUseLastResponse }}}
	ctx, stop := signal.NotifyContext(context.Background(), os.Interrupt, terminationSignal())
	defer stop()
	run(ctx, a)
}
