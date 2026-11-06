//go:build windows && !cgo

package main

import (
	"encoding/json"
	"fmt"
	"net"
	"os"
	"os/exec"
	"path/filepath"
	"testing"
	"time"

	"github.com/Microsoft/go-winio"
)

func TestDesktopSessionHelper(t *testing.T) {
	if os.Getenv("FCLASH_TEST_CORE") != "1" {
		return
	}
	startServer(os.Getenv("FCLASH_TEST_PIPE"))
}

func sessionCall(t *testing.T, connection net.Conn, method string, data any) map[string]any {
	t.Helper()
	id := fmt.Sprintf("test-%d", time.Now().UnixNano())
	payload, err := json.Marshal(map[string]any{"id": id, "method": method, "data": data})
	if err != nil {
		t.Fatal(err)
	}
	if err = connection.SetDeadline(time.Now().Add(10 * time.Second)); err != nil {
		t.Fatal(err)
	}
	if err = writeFrame(connection, payload); err != nil {
		t.Fatal(err)
	}
	for {
		frame, err := readFrame(connection)
		if err != nil {
			t.Fatal(err)
		}
		var result map[string]any
		if err = json.Unmarshal(frame, &result); err != nil {
			t.Fatal(err)
		}
		if result["id"] == id {
			return result
		}
	}
}

func acceptSession(t *testing.T, listener net.Listener) net.Conn {
	t.Helper()
	timer := time.AfterFunc(10*time.Second, func() { _ = listener.Close() })
	defer timer.Stop()
	connection, err := listener.Accept()
	if err != nil {
		t.Fatal(err)
	}
	return connection
}

func TestDesktopCoreStopsWhenOwnerDisconnects(t *testing.T) {
	home := t.TempDir()
	pipe := fmt.Sprintf(`\\.\pipe\FClash-test-%d-%d`, os.Getpid(), time.Now().UnixNano())
	listener, err := winio.ListenPipe(pipe, nil)
	if err != nil {
		t.Fatal(err)
	}
	t.Cleanup(func() { _ = listener.Close() })
	command := exec.Command(os.Args[0], "-test.run=^TestDesktopSessionHelper$")
	command.Env = append(os.Environ(), "FCLASH_TEST_CORE=1", "FCLASH_TEST_PIPE="+pipe)
	if err = command.Start(); err != nil {
		t.Fatal(err)
	}
	done := make(chan error, 1)
	go func() { done <- command.Wait() }()
	t.Cleanup(func() { _ = command.Process.Kill() })
	connection := acceptSession(t, listener)
	configPath := filepath.Join(home, "config.yaml")
	safeConfig := "mixed-port: 0\nport: 0\nsocks-port: 0\ndns:\n  enable: false\ntun:\n  enable: false\nrules:\n  - MATCH,DIRECT\n"
	if err = os.WriteFile(configPath, []byte(safeConfig), 0600); err != nil {
		t.Fatal(err)
	}
	init, _ := json.Marshal(map[string]any{"home-dir": home, "version": 11})
	if result := sessionCall(t, connection, "initClash", string(init)); result["data"] != true {
		t.Fatalf("init: %v", result)
	}
	if result := sessionCall(t, connection, "setupConfig", `{"selected-map":{},"test-url":"https://example.invalid"}`); result["data"] != "" {
		t.Fatalf("setup: %v", result)
	}
	if result := sessionCall(t, connection, "startListener", nil); result["data"] != true {
		t.Fatalf("start: %v", result)
	}
	before := sessionCall(t, connection, "desktopRuntime", nil)["data"].(map[string]any)
	if before["configDigest"] == "" {
		t.Fatal("core did not report its applied configuration")
	}
	_ = connection.Close()
	_ = listener.Close()
	select {
	case err := <-done:
		if err != nil {
			t.Fatalf("owner disconnect did not stop cleanly: %v", err)
		}
	case <-time.After(10 * time.Second):
		t.Fatal("Core outlived its GUI session")
	}
}
