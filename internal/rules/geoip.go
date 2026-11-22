package rules

import (
	_ "embed"
	"net"
	"strings"
	"sync"
)

//go:embed geoip_ru.txt
var geoipRU string

var (
	geoOnce sync.Once
	geoNets []*net.IPNet
)

func loadGeo() {
	geoOnce.Do(func() {
		for _, line := range strings.Split(geoipRU, "\n") {
			line = strings.TrimSpace(line)
			if line == "" || strings.HasPrefix(line, "#") {
				continue
			}
			if _, n, err := net.ParseCIDR(line); err == nil {
				geoNets = append(geoNets, n)
			}
		}
	})
}

// InGeoRU сообщает, принадлежит ли IP российским подсетям из встроенного списка.
// Используется в TUN-режиме: там видно только IP, доменов нет.
func InGeoRU(ip net.IP) bool {
	loadGeo()
	for _, n := range geoNets {
		if n.Contains(ip) {
			return true
		}
	}
	return false
}

// GeoCount — сколько подсетей загружено (для лога/диагностики).
func GeoCount() int {
	loadGeo()
	return len(geoNets)
}
