// Package geoip даёт быстрый ответ «этот IP российский?» по вшитому списку
// агрегированных подсетей (ipdeny.com, 8664 префикса).
package geoip

import (
	_ "embed"
	"encoding/binary"
	"net"
	"sort"
	"strings"
	"sync"
)

//go:embed data/ru.cidr
var ruRaw string

type rng struct{ lo, hi uint32 }

// DB — таблица диапазонов, отсортированная для бинарного поиска.
type DB struct {
	once   sync.Once
	ranges []rng
}

var shared = &DB{}

// RU возвращает общую базу российских подсетей.
func RU() *DB { return shared }

func (d *DB) load() {
	d.once.Do(func() {
		for _, line := range strings.Split(ruRaw, "\n") {
			line = strings.TrimSpace(line)
			if line == "" || strings.HasPrefix(line, "#") || strings.HasPrefix(line, "//") {
				continue
			}
			_, ipnet, err := net.ParseCIDR(line)
			if err != nil {
				continue
			}
			ip := ipnet.IP.To4()
			if ip == nil {
				continue
			}
			lo := binary.BigEndian.Uint32(ip)
			ones, bits := ipnet.Mask.Size()
			if bits != 32 {
				continue
			}
			hi := lo
			if ones < 32 {
				hi = lo | (1<<(32-ones) - 1)
			}
			d.ranges = append(d.ranges, rng{lo, hi})
		}
		sort.Slice(d.ranges, func(i, j int) bool { return d.ranges[i].lo < d.ranges[j].lo })
		// склеиваем пересекающиеся диапазоны
		merged := d.ranges[:0]
		for _, r := range d.ranges {
			if n := len(merged); n > 0 && r.lo <= merged[n-1].hi+1 {
				if r.hi > merged[n-1].hi {
					merged[n-1].hi = r.hi
				}
				continue
			}
			merged = append(merged, r)
		}
		d.ranges = merged
	})
}

// Len — сколько диапазонов после склейки.
func (d *DB) Len() int {
	d.load()
	return len(d.ranges)
}

// Contains сообщает, входит ли IP в российские подсети.
func (d *DB) Contains(ip net.IP) bool {
	ip4 := ip.To4()
	if ip4 == nil {
		return false
	}
	v := binary.BigEndian.Uint32(ip4)
	d.load()
	idx := sort.Search(len(d.ranges), func(i int) bool { return d.ranges[i].hi >= v })
	return idx < len(d.ranges) && d.ranges[idx].lo <= v
}

// ContainsString — удобная обёртка для строкового адреса.
func (d *DB) ContainsString(s string) bool {
	ip := net.ParseIP(s)
	if ip == nil {
		return false
	}
	return d.Contains(ip)
}
