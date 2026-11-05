package main

import (
	"context"
	"encoding/json"
	"fmt"
	"io"
	"net"
	"net/http"
	"strings"
	"time"

	C "github.com/metacubex/mihomo/constant"
	"github.com/metacubex/mihomo/tunnel"
)

// Probe the exit, not the proxy server's address. Each connection goes through
// the requested adapter without changing the user's selected proxy or rules.
func proxyCountry(ctx context.Context, dial func(context.Context, *C.Metadata) (net.Conn, error), endpoint string) (string, error) {
	transport := &http.Transport{
		DisableKeepAlives:   true,
		TLSHandshakeTimeout: 5 * time.Second,
		DialContext: func(ctx context.Context, network, address string) (net.Conn, error) {
			metadata := &C.Metadata{NetWork: C.TCP, Type: C.INNER}
			if err := metadata.SetRemoteAddress(address); err != nil {
				return nil, err
			}
			return dial(ctx, metadata)
		},
	}
	defer transport.CloseIdleConnections()
	client := &http.Client{
		Transport:     transport,
		CheckRedirect: func(*http.Request, []*http.Request) error { return http.ErrUseLastResponse },
	}
	request, err := http.NewRequestWithContext(ctx, http.MethodGet, endpoint, nil)
	if err != nil {
		return "", err
	}
	response, err := client.Do(request)
	if err != nil {
		return "", err
	}
	defer response.Body.Close()
	if response.StatusCode != http.StatusOK {
		return "", fmt.Errorf("exit lookup status %d", response.StatusCode)
	}
	var data struct {
		Success bool   `json:"success"`
		IP      string `json:"ip"`
		Country string `json:"country_code"`
	}
	if err := json.NewDecoder(io.LimitReader(response.Body, 16*1024)).Decode(&data); err != nil {
		return "", err
	}
	code := strings.ToUpper(data.Country)
	if !data.Success || net.ParseIP(data.IP) == nil || len(code) != 2 || code[0] < 'A' || code[0] > 'Z' || code[1] < 'A' || code[1] > 'Z' {
		return "", fmt.Errorf("invalid exit country response")
	}
	return code, nil
}

func handleGetProxyCountry(name string, result ActionResult) {
	go func() {
		proxy := tunnel.AllProxies()[name]
		if proxy == nil {
			result.success("")
			return
		}
		ctx, cancel := context.WithTimeout(context.Background(), 10*time.Second)
		defer cancel()
		code, err := proxyCountry(ctx, func(ctx context.Context, metadata *C.Metadata) (net.Conn, error) {
			return proxy.DialContext(ctx, metadata)
		}, "https://ipwho.is/")
		if err != nil {
			result.success("")
			return
		}
		result.success(code)
	}()
}
