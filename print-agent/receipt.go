package main

import (
	"bytes"
	"fmt"
	"html"
	"io"
	"regexp"
	"strings"
)

// Conservative Font A widths: some models have a narrower printable area.
// Paper width is a local setting; existing configurations default to 80 mm.
func columns(p printer) int {
	if p.PaperWidthMM == 58 { return 30 }
	return 42
}

type receiptLine struct {
	Text string
	Bold bool
	Height, Width int
}

var dateLine = regexp.MustCompile(`^[0-9]{2}-[0-9]{2}-[0-9]{4} [0-9]{2}:[0-9]{2}$`)
var guestLine = regexp.MustCompile(`^(Gasten|Guests|Convives|Gaeste|Personas): [0-9]+$`)

func printable(text string) string {
	var b strings.Builder
	for _, r := range strings.ReplaceAll(text, "\r\n", "\n") {
		switch {
		case r == '\n': b.WriteByte('\n')
		case r >= 32 && r <= 126: b.WriteRune(r)
		default:
			const accented = "ÀÁÂÃÄÅàáâãäåÈÉÊËèéêëÌÍÎÏìíîïÒÓÔÕÖòóôõöÙÚÛÜùúûüÇçÑñ"
			const plain = "AAAAAAaaaaaaEEEEeeeeIIIIiiiiOOOOOoooooUUUUuuuuCcNn"
			i := strings.IndexRune(accented, r)
			if i >= 0 { b.WriteByte(plain[len([]rune(accented[:i]))]) } else if r >= 32 && r != 127 { b.WriteByte('?') }
		}
	}
	return b.String()
}

// Wrap at spaces where possible, splitting unbroken strings if necessary.
// All text is sanitized to ASCII before calculating the printer columns.
func wrap(s string, n int) []string {
	var out []string
	for len(s) > n {
		at := strings.LastIndexByte(s[:n+1], ' ')
		if at <= 0 { at = n }
		out = append(out, s[:at])
		s = strings.TrimLeft(s[at:], " ")
	}
	return append(out, s)
}

func layout(text string, p printer) []receiptLine {
	n := columns(p)
	var out []receiptLine
	lines := strings.Split(strings.TrimRight(printable(text), "\n"), "\n")
	// The API sends the heading first, then the restaurant and a separator.
	// Reprints have an additional heading. Never interpret text as commands.
	reprint := len(lines) > 0 && (lines[0] == "HERDRUK" || lines[0] == "REPRINT" || lines[0] == "REIMPRESSION" || lines[0] == "NACHDRUCK" || lines[0] == "REIMPRESION")
	for i, s := range lines {
		line := receiptLine{Bold:false, Height:1, Width:1}
		if s != "" && strings.Trim(s, "-") == "" {
			s = strings.Repeat("-", n)
		} else if i == 0 || (reprint && i == 1) || dateLine.MatchString(s) || guestLine.MatchString(s) {
			line.Bold, line.Height = true, 2
			if (dateLine.MatchString(s) || guestLine.MatchString(s)) && len(s) <= n/2 { line.Width = 2 }
		}
		for _, part := range wrap(s, n/line.Width) {
			line.Text = part
			out = append(out,line)
		}
	}
	return out
}

func receipt(text string, p printer) []byte {
	var b bytes.Buffer
	// Reset printer settings and select Font A; no persistent configuration changes.
	b.Write([]byte{0x1b,0x40,0x1b,0x4d,0})
	for _, line := range layout(text,p) {
		bold := byte(0)
		if line.Bold { bold = 1 }
		size := byte((line.Width-1)<<4 | (line.Height-1))
		b.Write([]byte{0x1b,0x45,bold,0x1d,0x21,size})
		b.WriteString(line.Text)
		b.WriteByte('\n')
	}
	b.Write([]byte{0x1b,0x45,0,0x1d,0x21,0})
	// Explicit line feed plus feed-to-cutting-position (partial cut).
	// ESC/POS GS V 66 0 is supported by the Epson TM-T88V.
	b.Write([]byte{0x1b,0x64,4})
	if p.Cut { b.Write([]byte{0x1d,0x56,66,0}) }
	return b.Bytes()
}

// Offline preview uses the same layout as the printer, no config/token/network.
func previewCommand(w io.Writer, width string) error {
	p := printer{PaperWidthMM:80}
	switch width {
	case "58": p.PaperWidthMM = 58
	case "80":
	default: return fmt.Errorf("preview width must be 58 or 80")
	}
	const sample = "NIEUWE RESERVERING\nVoorbeeldrestaurant\n--------------------------------\n25-11-2026 19:00\nGasten: 4\nNaam: Voorbeeld Gast\nReferentie: TEST1234\nOpmerking: Een lange opmerking voor de keuken, graag een rustige tafel bij het raam."
	return writePreview(w,sample,p)
}

func writePreview(w io.Writer, text string, p printer) error {
	var b strings.Builder
	fmt.Fprintf(&b,`<!doctype html><html lang="nl"><meta charset="utf-8"><title>OpenResto receipt preview</title><style>body{background:#eee;font-family:monospace;padding:20px}.receipt{background:white;padding:12px;width:%dch;line-height:1.35;box-shadow:0 2px 8px #bbb}.line{white-space:pre;transform-origin:top left}.cut{margin-top:4em;border-top:1px dashed #999}</style><p>Offline preview — %d mm (approximate)</p><div class="receipt">`,columns(p),p.PaperWidthMM)
	for _, line := range layout(text,p) {
		weight := "normal"
		if line.Bold { weight = "bold" }
		fmt.Fprintf(&b,`<div class="line" style="font-weight:%s;min-height:%gem;transform:scale(%d,%d)">%s</div>`,weight,float64(line.Height)*1.35,line.Width,line.Height,html.EscapeString(line.Text))
	}
	b.WriteString(`<div class="cut"></div></div></html>`)
	_,err := io.WriteString(w,b.String())
	return err
}
