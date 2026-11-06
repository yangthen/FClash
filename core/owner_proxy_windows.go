//go:build windows && !cgo

package main

import (
	"encoding/json"
	"os"
	"path/filepath"

	"golang.org/x/sys/windows"
	"golang.org/x/sys/windows/registry"
)

type ownerProxySnapshot struct {
	Enabled             *uint32
	Server              *string
	Bypass              *string
	OwnedServer         string
	PreviousOwnedServer *string
}

func (snapshot ownerProxySnapshot) owns(enabled uint64, server string) bool {
	return enabled == 1 && snapshot.OwnedServer != "" &&
		(server == snapshot.OwnedServer || (snapshot.PreviousOwnedServer != nil && server == *snapshot.PreviousOwnedServer))
}

func restoreOwnerProxy(home string) {
	path := filepath.Join(home, "proxy-recovery.json")
	data, err := os.ReadFile(path)
	if err != nil {
		return
	}
	var snapshot ownerProxySnapshot
	if json.Unmarshal(data, &snapshot) != nil {
		return
	}
	key, err := registry.OpenKey(registry.CURRENT_USER, `Software\Microsoft\Windows\CurrentVersion\Internet Settings`, registry.QUERY_VALUE|registry.SET_VALUE)
	if err != nil {
		return
	}
	defer key.Close()
	enabled, _, _ := key.GetIntegerValue("ProxyEnable")
	server, _, _ := key.GetStringValue("ProxyServer")
	if snapshot.owns(enabled, server) {
		if snapshot.Enabled == nil {
			err = key.DeleteValue("ProxyEnable")
		} else {
			err = key.SetDWordValue("ProxyEnable", *snapshot.Enabled)
		}
		set := func(name string, value *string) error {
			if value == nil {
				return key.DeleteValue(name)
			}
			return key.SetStringValue(name, *value)
		}
		serverErr := set("ProxyServer", snapshot.Server)
		bypassErr := set("ProxyOverride", snapshot.Bypass)
		if (err != nil && err != registry.ErrNotExist) || (serverErr != nil && serverErr != registry.ErrNotExist) || (bypassErr != nil && bypassErr != registry.ErrNotExist) {
			return // Preserve journal for recovery on the next launch.
		}
		notify := windows.NewLazySystemDLL("wininet.dll").NewProc("InternetSetOptionW")
		_, _, _ = notify.Call(0, 39, 0, 0)
		_, _, _ = notify.Call(0, 37, 0, 0)
	}
	_ = os.Remove(path)
}
