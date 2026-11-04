package main

import (
	"os"
	"path/filepath"
	"testing"

	"github.com/metacubex/mihomo/config"
	"github.com/metacubex/mihomo/constant"
)

func TestInvalidConfigPreservesRunningConfig(t *testing.T) {
	oldHome := constant.Path.HomeDir()
	oldConfig := currentConfig
	oldTestURL := constant.DefaultTestURL
	t.Cleanup(func() {
		constant.SetHomeDir(oldHome)
		currentConfig = oldConfig
		constant.DefaultTestURL = oldTestURL
	})
	dir := t.TempDir()
	constant.SetHomeDir(dir)
	previous := &config.Config{}
	currentConfig = previous
	if err := os.WriteFile(filepath.Join(dir, "config.yaml"), []byte("rules: [\n"), 0600); err != nil {
		t.Fatal(err)
	}
	if err := applyConfig(&SetupParams{TestURL: "https://invalid.example"}); err == nil {
		t.Fatal("expected validation error")
	}
	if currentConfig != previous {
		t.Fatal("failed configuration replaced running configuration")
	}
	if constant.DefaultTestURL != oldTestURL {
		t.Fatal("failed configuration changed test URL")
	}
}
