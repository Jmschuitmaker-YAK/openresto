//go:build !windows

package main

import (
	"os"
	"syscall"
)

func terminationSignal() os.Signal { return syscall.SIGTERM }

func syncDirectory(path string) error {
	d, err := os.Open(path)
	if err != nil { return err }
	defer d.Close()
	return d.Sync()
}
