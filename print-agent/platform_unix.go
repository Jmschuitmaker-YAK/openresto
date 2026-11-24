//go:build !windows

package main

import (
	"os"
	"path/filepath"
	"syscall"
)

func terminationSignal() os.Signal { return syscall.SIGTERM }

func syncDirectory(path string) error {
	d, err := os.Open(path)
	if err != nil { return err }
	defer d.Close()
	return d.Sync()
}

// flock is released by the kernel even after an abrupt process exit.
func lockState(dir string) (func(), error) {
	f, err := os.OpenFile(filepath.Join(dir, ".agent.lock"), os.O_CREATE|os.O_RDWR, 0600)
	if err != nil { return nil, err }
	if err := syscall.Flock(int(f.Fd()), syscall.LOCK_EX|syscall.LOCK_NB); err != nil {
		f.Close()
		return nil, err
	}
	return func(){ _ = f.Close() }, nil
}
