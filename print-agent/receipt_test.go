package main

import (
	"bytes"
	"strings"
	"testing"
)

func TestReceiptSanitizesControlBytes(t *testing.T) {
	text := printable("Réservation\x1b\x1d\r\n4 gasten")
	if text != "Reservation\n4 gasten" { t.Fatalf("unexpected text %q",text) }
	got := receipt("Réservation\x1b\x1d\r\n4 gasten",printer{Cut:true})
	if !bytes.Contains(got,[]byte("Reservation\n")) || bytes.Contains(got,[]byte{0x1b,0x1d}) { t.Fatalf("unsafe receipt %q",got) }
}

func TestReceiptFeedAndCut(t *testing.T) {
	for _, cut := range []bool{false,true} {
		got := receipt("TESTBON",printer{Cut:cut})
		want := []byte{0x1b,0x45,0,0x1d,0x21,0,0x1b,0x64,4}
		if cut { want=append(want,0x1d,0x56,66,0) }
		if !bytes.HasSuffix(got,want) { t.Fatalf("wrong feed/cut suffix %x",got) }
	}
}

func TestLayoutWidthAndEmphasis(t *testing.T) {
	s := "NIEUWE RESERVERING\nRestaurant\n--------------------------------\n25-11-2026 19:00\nGasten: 12\nNaam: "+strings.Repeat("a",100)+"\nOpmerking: "+strings.Repeat("lange tekst ",20)
	for _, mm := range []int{0,58,80} {
		p:=printer{PaperWidthMM:mm}
		lines:=layout(s,p)
		for _, l:=range lines {
			if len(l.Text)*l.Width>columns(p) { t.Fatalf("overflow %d: %+v",mm,l) }
		}
		if !lines[0].Bold || lines[0].Height!=2 || !lines[3].Bold || lines[3].Height!=2 || !lines[4].Bold { t.Fatal("missing emphasis") }
		if lines[2].Text!=strings.Repeat("-",columns(p)) { t.Fatal("divider width") }
		if strings.Count(strings.Join(func() []string { var s []string; for _,l:=range lines{s=append(s,l.Text)};return s }(),""),"a") < 100 { t.Fatal("lost text") }
	}
}

func TestPreviewIsOfflineAndEscapesHTML(t *testing.T) {
	var b bytes.Buffer
	if err:=previewCommand(&b,"58");err!=nil { t.Fatal(err) }
	if !strings.Contains(b.String(),"58 mm") { t.Fatal("wrong preview width") }
	b.Reset()
	if err:=writePreview(&b,"<script>alert(1)</script>",printer{PaperWidthMM:80});err!=nil { t.Fatal(err) }
	if strings.Contains(b.String(),"<script>") || !strings.Contains(b.String(),"&lt;script&gt;") { t.Fatal("unescaped preview") }
}

func TestReprintHeading(t *testing.T) {
	lines:=layout("HERDRUK\nGEWIJZIGD\nRestaurant",printer{})
	if !lines[0].Bold || !lines[1].Bold || lines[2].Bold { t.Fatalf("wrong emphasis: %+v",lines) }
}
