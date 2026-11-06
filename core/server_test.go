//go:build !cgo

package main

import (
	"bytes"
	"testing"
)

func TestFrameRoundTrip(t *testing.T) {
	var buffer bytes.Buffer
	for _, input := range [][]byte{[]byte(`{"id":"first"}`), []byte("中文 payload")} {
		if err := writeFrame(&buffer, input); err != nil {
			t.Fatal(err)
		}
		output, err := readFrame(&buffer)
		if err != nil {
			t.Fatal(err)
		}
		if !bytes.Equal(input, output) {
			t.Fatalf("frame changed: %q", output)
		}
	}
}

func TestTruncatedFrameFails(t *testing.T) {
	if _, err := readFrame(bytes.NewReader([]byte{8, 0, 0, 0, 1})); err == nil {
		t.Fatal("expected truncated frame error")
	}
}

func TestOversizedFrameFailsBeforeAllocating(t *testing.T) {
	if _, err := readFrame(bytes.NewReader([]byte{0xff, 0xff, 0xff, 0x7f})); err == nil {
		t.Fatal("expected oversized frame error")
	}
}
