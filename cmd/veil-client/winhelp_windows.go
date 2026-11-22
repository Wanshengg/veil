//go:build windows

package main

import (
	"strings"
	"syscall"
)

// hiddenWindow — не показывать консольные окна от netsh/route.
func hiddenWindow() *syscall.SysProcAttr {
	return &syscall.SysProcAttr{HideWindow: true}
}

// decodeConsole приводит вывод netsh/route к читаемому виду.
// Эти утилиты пишут в OEM-кодировке (CP866), поэтому просто чистим NUL и \r.
func decodeConsole(b []byte) string {
	s := strings.ReplaceAll(string(b), "\x00", "")
	s = strings.ReplaceAll(s, "\r", "")
	return s
}
