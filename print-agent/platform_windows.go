//go:build windows

package main

import "os"

func terminationSignal() os.Signal { return os.Interrupt }

// Windows has no portable directory-fsync operation in Go's standard library.
// This build is experimental; power-loss durability is not yet certified.
func syncDirectory(string) error { return nil }
