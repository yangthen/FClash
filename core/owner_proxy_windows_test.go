//go:build windows && !cgo

package main

import (
	"encoding/json"
	"testing"
)

func TestOwnerProxySnapshotDoesNotClaimOtherClients(t *testing.T) {
	var snapshot ownerProxySnapshot
	if err := json.Unmarshal([]byte(`{"Enabled":0,"Server":null,"Bypass":"<local>","OwnedServer":"127.0.0.1:7890","PreviousOwnedServer":"127.0.0.1:7891"}`), &snapshot); err != nil {
		t.Fatal(err)
	}
	if !snapshot.owns(1, "127.0.0.1:7890") || !snapshot.owns(1, "127.0.0.1:7891") {
		t.Fatal("lost current/previous port during a proxy transaction")
	}
	if snapshot.owns(0, "127.0.0.1:7890") || snapshot.owns(1, "127.0.0.1:9999") || (ownerProxySnapshot{}).owns(1, "") {
		t.Fatal("claimed another client's proxy settings")
	}
}
