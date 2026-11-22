package rules

import (
	"bufio"
	"net"
	"os"
	"strings"

	"veil/internal/geoip"
)

// builtinDirect is the default "keep it local" list: Russian TLDs and the
// services that break when they leave the country (banking, gosuslugi, CDNs).
var builtinDirect = []string{
	// TLDs
	"ru", "su", "рф", "xn--p1ai",
	// big local services
	"yandex.ru", "yandex.net", "ya.ru", "yandex.com",
	"vk.com", "vk.ru", "userapi.com", "vk-cdn.net", "mycdn.me",
	"mail.ru", "list.ru", "bk.ru", "inbox.ru", "internet.ru",
	"ok.ru", "dzen.ru", "rutube.ru", "kinopoisk.ru", "ivi.ru",
	"ozon.ru", "wildberries.ru", "avito.ru", "aliexpress.ru",
	"sberbank.ru", "sber.ru", "online.sberbank.ru", "tinkoff.ru",
	"alfabank.ru", "vtb.ru", "gazprombank.ru", "raiffeisen.ru",
	"gosuslugi.ru", "nalog.gov.ru", "mos.ru", "rt.ru", "mts.ru",
	"beeline.ru", "megafon.ru", "tele2.ru", "yota.ru",
	"2gis.ru", "2gis.com", "hh.ru", "rabota.ru", "cian.ru",
	"lenta.ru", "ria.ru", "rbc.ru", "tass.ru", "kommersant.ru",
	"mail.yandex.ru", "passport.yandex.ru", "taxi.yandex.ru",
	"steamstatic.com.8686c.com", "yastatic.net", "mds.yandex.net",
	"cdnvideo.ru", "ngcdn.ru", "rutube.ru.cdnvideo.ru",
	"gos-uslugi.ru", "esia.gosuslugi.ru",
	"ozon.by", "market.yandex.ru", "beru.ru",
	"mosmetro.ru", "transport.mos.ru", "mosenergosbyt.ru",
	"pochta.ru", "russianpost.ru", "cdek.ru", "boxberry.ru",
	"rzd.ru", "aeroflot.ru", "s7.ru", "aviasales.ru",
	"kinopoisk.ru", "okko.tv", "more.tv", "premier.one",
	"vkvideo.ru", "vkplay.ru", "mail.ru.games", "my.games",
}

type Rules struct {
	suffixes []string
	nets     []*net.IPNet
	enabled  bool
	geo      *geoip.DB
}

func NewRules(enabled bool, extraFile string, geo *geoip.DB) *Rules {
	r := &Rules{enabled: enabled, geo: geo}
	if !enabled {
		return r
	}
	for _, s := range builtinDirect {
		r.suffixes = append(r.suffixes, strings.ToLower(strings.TrimSpace(s)))
	}
	// never tunnel the local network
	for _, cidr := range []string{"10.0.0.0/8", "172.16.0.0/12", "192.168.0.0/16", "127.0.0.0/8", "169.254.0.0/16", "::1/128", "fc00::/7", "fe80::/10"} {
		if _, n, err := net.ParseCIDR(cidr); err == nil {
			r.nets = append(r.nets, n)
		}
	}
	if extraFile != "" {
		if f, err := os.Open(extraFile); err == nil {
			defer f.Close()
			sc := bufio.NewScanner(f)
			for sc.Scan() {
				line := strings.TrimSpace(sc.Text())
				if line == "" || strings.HasPrefix(line, "#") {
					continue
				}
				if _, n, err := net.ParseCIDR(line); err == nil {
					r.nets = append(r.nets, n)
					continue
				}
				r.suffixes = append(r.suffixes, strings.ToLower(strings.TrimPrefix(line, ".")))
			}
		}
	}
	return r
}

// Direct reports whether the destination must be reached without the tunnel.
func (r *Rules) Direct(hostport string) bool {
	if r == nil || !r.enabled {
		return false
	}
	host := hostport
	if h, _, err := net.SplitHostPort(hostport); err == nil {
		host = h
	}
	host = strings.ToLower(strings.Trim(host, "[]"))
	if ip := net.ParseIP(host); ip != nil {
		for _, n := range r.nets {
			if n.Contains(ip) {
				return true
			}
		}
		if r.geo != nil && r.geo.Contains(ip) {
			return true
		}
		return false
	}
	if i := strings.IndexByte(host, ':'); i > 0 { // bare host:port
		host = host[:i]
	}
	host = strings.TrimSuffix(host, ".")
	for _, s := range r.suffixes {
		if host == s || strings.HasSuffix(host, "."+s) {
			return true
		}
	}
	return false
}
