package main

import (
	"context"
	"errors"
	"fmt"
	"net"
	"net/http"
	"net/http/httptest"
	"strings"
	"sync/atomic"
	"testing"

	C "github.com/metacubex/mihomo/constant"
)

func TestProxyCountryUsesSpecifiedAdapter(t *testing.T) {
	server := httptest.NewServer(http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
		fmt.Fprint(w, `{"success":true,"ip":"1.1.1.1","country_code":"jp"}`)
	}))
	defer server.Close()
	var calls atomic.Int32
	code, err := proxyCountry(context.Background(), func(ctx context.Context, metadata *C.Metadata) (net.Conn, error) {
		calls.Add(1)
		if metadata.Host != "exit.example" || metadata.DstPort != 80 || metadata.NetWork != C.TCP {
			t.Errorf("unexpected destination: %+v", metadata)
		}
		return (&net.Dialer{}).DialContext(ctx, "tcp", server.Listener.Addr().String())
	}, "http://exit.example/")
	if err != nil || code != "JP" || calls.Load() != 1 {
		t.Fatalf("country=%q calls=%d err=%v", code, calls.Load(), err)
	}
}

func TestProxyCountryDoesNotFallbackToDirect(t *testing.T) {
	_, err := proxyCountry(context.Background(), func(context.Context, *C.Metadata) (net.Conn, error) {
		return nil, errors.New("adapter unavailable")
	}, "https://exit.example/")
	if err == nil || !strings.Contains(err.Error(), "adapter unavailable") {
		t.Fatalf("expected adapter failure, got %v", err)
	}
}

func TestProxyCountryRejectsInvalidResponses(t *testing.T) {
	for _, body := range []string{
		`not json`,
		`{"success":false,"ip":"1.1.1.1","country_code":"US"}`,
		`{"success":true,"ip":"invalid","country_code":"US"}`,
		`{"success":true,"ip":"1.1.1.1","country_code":"XX1"}`,
		`{"success":true,"ip":"1.1.1.1","country_code":"12"}`,
		strings.Repeat(" ", 16*1024) + `{"success":true,"ip":"1.1.1.1","country_code":"US"}`,
	} {
		t.Run(fmt.Sprintf("body-length-%d", len(body)), func(t *testing.T) {
			server := httptest.NewServer(http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
				fmt.Fprint(w, body)
			}))
			defer server.Close()
			code, err := proxyCountry(context.Background(), func(ctx context.Context, metadata *C.Metadata) (net.Conn, error) {
				return (&net.Dialer{}).DialContext(ctx, "tcp", server.Listener.Addr().String())
			}, server.URL)
			if err == nil || code != "" {
				t.Fatalf("invalid response accepted: %q, %v", code, err)
			}
		})
	}
}

func TestProxyCountryRejectsRedirectAndCancelledRequest(t *testing.T) {
	server := httptest.NewServer(http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
		http.Redirect(w, r, "/redirect", http.StatusFound)
	}))
	defer server.Close()
	dial := func(ctx context.Context, metadata *C.Metadata) (net.Conn, error) {
		return (&net.Dialer{}).DialContext(ctx, "tcp", server.Listener.Addr().String())
	}
	if _, err := proxyCountry(context.Background(), dial, server.URL); err == nil {
		t.Fatal("redirect should fail without being followed")
	}
	ctx, cancel := context.WithCancel(context.Background())
	cancel()
	if _, err := proxyCountry(ctx, dial, server.URL); err == nil {
		t.Fatal("cancelled context should fail")
	}
}
