package main

import (
	"bufio"
	"crypto/sha256"
	"fmt"
	"os"
	"sort"
	"strings"
	"sync"

	"veil/internal/proto"
)

// Keyring — набор ключей сервера: по одному на человека.
// Файл формата `имя:psk`, по строке на ключ. Выдать ключ = дописать строку,
// отозвать = удалить строку и послать SIGHUP (systemctl reload).
type Keyring struct {
	mu   sync.RWMutex
	keys []proto.NamedKey
}

// LoadKeyring собирает кольцо из legacy-флага -psk и/или файла ключей.
func LoadKeyring(single, file string) (*Keyring, error) {
	k := &Keyring{}
	if strings.TrimSpace(single) != "" {
		k.keys = append(k.keys, proto.NamedKey{Name: "default", PSK: hashPSK(single)})
	}
	if file != "" {
		list, err := readKeyFile(file)
		if err != nil {
			return nil, err
		}
		k.keys = append(k.keys, list...)
	}
	if len(k.keys) == 0 {
		return nil, fmt.Errorf("нет ключей: укажи -psk или -psks")
	}
	return k, nil
}

// Reload перечитывает файл ключей.
func (k *Keyring) Reload(file string) error {
	if file == "" {
		return fmt.Errorf("-psks не задан")
	}
	list, err := readKeyFile(file)
	if err != nil {
		return err
	}
	if len(list) == 0 {
		return fmt.Errorf("файл %s пуст — оставляю прежние ключи", file)
	}
	k.mu.Lock()
	k.keys = list
	k.mu.Unlock()
	return nil
}

// Keys — копия списка для обработки соединения.
func (k *Keyring) Keys() []proto.NamedKey {
	k.mu.RLock()
	defer k.mu.RUnlock()
	out := make([]proto.NamedKey, len(k.keys))
	copy(out, k.keys)
	return out
}

// Names — имена ключей строкой для лога.
func (k *Keyring) Names() string {
	k.mu.RLock()
	defer k.mu.RUnlock()
	n := make([]string, 0, len(k.keys))
	for _, x := range k.keys {
		n = append(n, x.Name)
	}
	sort.Strings(n)
	return strings.Join(n, ",")
}

// hashPSK — клиент отправляет sha256(psk), сервер держит ту же производную.
func hashPSK(psk string) []byte {
	sum := sha256.Sum256([]byte(strings.TrimSpace(psk)))
	return sum[:]
}

func readKeyFile(path string) ([]proto.NamedKey, error) {
	f, err := os.Open(path)
	if err != nil {
		return nil, err
	}
	defer f.Close()

	var out []proto.NamedKey
	sc := bufio.NewScanner(f)
	line := 0
	for sc.Scan() {
		line++
		s := strings.TrimSpace(sc.Text())
		if s == "" || strings.HasPrefix(s, "#") || strings.HasPrefix(s, ";") {
			continue
		}
		name, psk, ok := strings.Cut(s, ":")
		if !ok {
			return nil, fmt.Errorf("%s:%d: ожидался формат имя:psk", path, line)
		}
		name, psk = strings.TrimSpace(name), strings.TrimSpace(psk)
		if name == "" || len(psk) < 16 {
			return nil, fmt.Errorf("%s:%d: пустое имя или короткий ключ", path, line)
		}
		out = append(out, proto.NamedKey{Name: name, PSK: hashPSK(psk)})
	}
	return out, sc.Err()
}
