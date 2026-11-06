//go:build !cgo

package main

import (
	"encoding/binary"
	"encoding/json"
	"fmt"
	"io"
	"os"
	"sync"
	"time"
)

var (
	conn   io.ReadWriteCloser
	connMu sync.Mutex
)

func (result ActionResult) send() {
	data, err := result.Json()
	if err != nil {
		logError("ActionResult marshal error: method=%s id=%s err=%v", result.Method, result.Id, err)
		return
	}
	send(data)
}

func sendMessage(message Message) {
	result := ActionResult{
		Method: messageMethod,
		Data:   message,
	}
	result.send()
}

func writeFrame(w io.Writer, data []byte) error {
	frame := make([]byte, 4+len(data))
	binary.LittleEndian.PutUint32(frame, uint32(len(data)))
	copy(frame[4:], data)
	_, err := w.Write(frame)
	return err
}

func readFrame(r io.Reader) ([]byte, error) {
	lenBuf := make([]byte, 4)
	if _, err := io.ReadFull(r, lenBuf); err != nil {
		return nil, err
	}
	length := binary.LittleEndian.Uint32(lenBuf)
	if length > 32*1024*1024 {
		return nil, fmt.Errorf("IPC frame exceeds 32 MiB")
	}
	data := make([]byte, length)
	if _, err := io.ReadFull(r, data); err != nil {
		return nil, err
	}
	return data, nil
}

func send(data []byte) {
	connMu.Lock()
	defer connMu.Unlock()
	if conn == nil {
		return
	}
	if err := writeFrame(conn, data); err != nil {
		logError("server write error: %v", err)
	}
}

func startServer(arg string) {
	// A desktop Core belongs to one GUI session. Never reconnect to a later GUI
	// after its owner exits: listeners and TUN must end with that session.
	home := serveConnection(arg)
	// Restore networking before shutdown, including a GUI crash. Bound shutdown
	// so a stuck provider cannot turn this into an orphaned headless session.
	deadline := time.AfterFunc(5*time.Second, func() { os.Exit(0) })
	defer deadline.Stop()
	if home != "" {
		restoreOwnerProxy(home)
	}
	handleShutdown()
}

func serveConnection(arg string) (home string) {
	var err error
	connection, err := dial(arg)
	if err != nil {
		return ""
	}
	connMu.Lock()
	conn = connection
	connMu.Unlock()

	defer func() {
		connMu.Lock()
		conn = nil
		connMu.Unlock()
		_ = connection.Close()
		requestEventsEnabled.Store(false)
		handleStopLog()
	}()

	for {
		data, err := readFrame(connection)
		if err != nil {
			if err != io.EOF {
				logError("server read error: %v", err)
			}
			return home
		}
		var action = &Action{}

		err = json.Unmarshal(data, action)

		if err != nil {
			logError("server unmarshal error: %v (data: %q)", err, data)
			continue
		}
		if action.Method == initClashMethod {
			if data, ok := action.Data.(string); ok {
				var params InitParams
				if json.Unmarshal([]byte(data), &params) == nil {
					home = params.HomeDir
				}
			}
		}

		result := ActionResult{
			Id:     action.Id,
			Method: action.Method,
		}

		go handleAction(action, result)
	}
}

func nextHandle(action *Action, result ActionResult) bool {
	switch action.Method {
	case "desktopRuntime":
		runLock.Lock()
		data := map[string]any{"running": isRunning, "startedAt": startedAt, "configDigest": activeConfigDigest}
		runLock.Unlock()
		result.success(data)
		return true
	case "terminateCore":
		handleShutdown()
		result.success(true)
		go func() { time.Sleep(50 * time.Millisecond); os.Exit(0) }()
		return true
	}
	return false
}
