//go:build windows

package main

import (
	"os"
	"path/filepath"
)

func terminationSignal() os.Signal { return os.Interrupt }

// Windows has no portable directory-fsync operation in Go's standard library.
// This build is experimental; power-loss durability is not yet certified.
func syncDirectory(string) error { return nil }

// Conservative experimental Windows guard. After a crash an operator must
// confirm the old process stopped before removing the lock file.
func lockState(dir string) (func(), error) {
	path := filepath.Join(dir, ".agent.lock")
	f, err := os.OpenFile(path, os.O_CREATE|os.O_EXCL|os.O_RDWR, 0600)
	if err != nil { return nil, err }
	return func(){ _ = f.Close(); _ = os.Remove(path) }, nil
}
