package main

import (
	"bytes"
	"context"
	"encoding/json"
	"errors"
	"io"
	"net"
	"net/http"
	"net/http/httptest"
	"os"
	"strings"
	"testing"
	"time"
)

type fakeConn struct {
	bytes.Buffer
	fail bool
}
func (c *fakeConn) Write(b []byte) (int,error) {
	if c.fail { return 1, io.ErrUnexpectedEOF }
	return c.Buffer.Write(b)
}
func (*fakeConn) Read([]byte)(int,error){return 0,io.EOF}
func (*fakeConn) Close()error{return nil}
func (*fakeConn) LocalAddr()net.Addr{return nil}
func (*fakeConn) RemoteAddr()net.Addr{return nil}
func (*fakeConn) SetDeadline(time.Time)error{return nil}
func (*fakeConn) SetReadDeadline(time.Time)error{return nil}
func (*fakeConn) SetWriteDeadline(time.Time)error{return nil}

func newTestAgent(t *testing.T, conn *fakeConn) (*agent,*int) {
	t.Helper()
	calls:=0
	a:=&agent{cfg:config{StateDir:t.TempDir(),Printers:map[string]printer{"bar":{Address:"localhost:9100",Cut:true}}},
		dial:func(context.Context,string,string)(net.Conn,error){calls++;return conn,nil}}
	return a,&calls
}

func TestReceiptSanitizesControlBytes(t *testing.T) {
	got:=receipt("Réservation\x1b\x1d\r\n4 gasten",true)
	want:=append([]byte{27,64},[]byte("Reservation\n4 gasten\n\n\n")...)
	want=append(want,29,86,0)
	if !bytes.Equal(got,want){t.Fatalf("receipt = %q",got)}
}
func TestReplayDoesNotPrintAgain(t *testing.T){
	conn:=&fakeConn{}
	a,calls:=newTestAgent(t,conn)
	j:=job{ID:"test-1",Printer:"bar",Text:"Nieuwe reservering"}
	for i:=0;i<2;i++ {
		if r:=a.process(context.Background(),j);r.Status!="sent"{t.Fatalf("%+v",r)}
	}
	if *calls!=1{t.Fatalf("dial count %d",*calls)}
}
func TestCrashIntentDoesNotPrintAgain(t *testing.T){
	a,calls:=newTestAgent(t,&fakeConn{})
	if err:=a.saveResult("test-2",result{Status:"sending"});err!=nil{t.Fatal(err)}
	r:=a.process(context.Background(),job{ID:"test-2",Printer:"bar",Text:"test"})
	if r.Status!="uncertain"||*calls!=0{t.Fatalf("%+v calls=%d",r,*calls)}
}
func TestUnreachablePrinterCanRetry(t *testing.T){
	a,_:=newTestAgent(t,&fakeConn{})
	a.dial=func(context.Context,string,string)(net.Conn,error){return nil,errors.New("offline")}
	r:=a.process(context.Background(),job{ID:"test-3",Printer:"bar",Text:"test"})
	if r.Status!="retry"{t.Fatalf("%+v",r)}
	if _,err:=os.Stat(a.statePath("test-3"));!os.IsNotExist(err){t.Fatalf("unexpected journal: %v",err)}
}
func TestPartialWriteRequiresReview(t *testing.T){
	a,calls:=newTestAgent(t,&fakeConn{fail:true})
	j:=job{ID:"test-4",Printer:"bar",Text:"test"}
	for i:=0;i<2;i++ {
		if r:=a.process(context.Background(),j);r.Status!="uncertain"{t.Fatalf("%+v",r)}
	}
	if *calls!=1{t.Fatalf("dial count %d",*calls)}
}
func TestAuthenticatedClaimAndAck(t *testing.T){
	a,calls:=newTestAgent(t,&fakeConn{})
	a.token="test-token"
	ack:=false
	s:=httptest.NewTLSServer(http.HandlerFunc(func(w http.ResponseWriter,r *http.Request){
		if r.Header.Get("Authorization")!="Bearer test-token"{t.Error("missing auth");w.WriteHeader(401);return}
		switch r.URL.Path{
		case "/api/print-agent/v1/claim":
			_ = json.NewEncoder(w).Encode(job{ID:"test-5",Lease:"lease",Printer:"bar",Text:"test"})
		case "/api/print-agent/v1/result":
			var body struct{ID,Lease,Status string}
			if err:=json.NewDecoder(r.Body).Decode(&body);err!=nil||body.ID!="test-5"||body.Lease!="lease"||body.Status!="sent"{t.Errorf("invalid result: %+v %v",body,err)}
			ack=true
			w.WriteHeader(204)
		default:t.Error("unexpected endpoint");w.WriteHeader(404)
		}
	}))
	defer s.Close()
	a.cfg.Server=s.URL;a.client=s.Client()
	if err:=a.tick(context.Background());err!=nil{t.Fatal(err)}
	if !ack||*calls!=1{t.Fatal("print/result missing")}
}
func TestConfigRequiresHTTPS(t *testing.T){
	c:=config{Server:"http://example.test",StateDir:"state",TokenFile:"token",Printers:map[string]printer{"bar":{Address:"localhost:9100"}}}
	if err:=validateConfig(c);err==nil{t.Fatal("accepted insecure origin")}
	c.Server="https://example.test"
	if err:=validateConfig(c);err!=nil{t.Fatal(err)}
	c.Server="https://example.test/path"
	if err:=validateConfig(c);err==nil||!strings.Contains(err.Error(),"HTTPS"){t.Fatal("accepted path")}
}
