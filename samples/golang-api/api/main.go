package main

import (
	"context"
	"encoding/json"
	"errors"
	"io"
	"log"
	"net"
	"net/http"
	"os"
	"os/signal"
	"strconv"
	"sync"
	"syscall"
	"time"
	"unicode/utf8"

	"github.com/go-chi/chi/v5"
	"github.com/go-chi/chi/v5/middleware"
)

const (
	maxJSONRequestBodyBytes = 1 << 20
	maxItemNameLength       = 200
	maxStoredItems          = 1000
	defaultBindHost         = "127.0.0.1"
)

var (
	errStoreFull        = errors.New("store is at capacity")
	errItemNameRequired = errors.New("item name is required")
	errItemNameTooLong  = errors.New("item name is too long")
)

type Item struct {
	ID        int       `json:"id"`
	Name      string    `json:"name"`
	Completed bool      `json:"completed"`
	CreatedAt time.Time `json:"createdAt"`
}

type Store struct {
	mu       sync.RWMutex
	items    map[int]*Item
	nextID   int
	maxItems int
}

func NewStore(maxItems int) *Store {
	return &Store{
		items:    make(map[int]*Item),
		nextID:   1,
		maxItems: maxItems,
	}
}

func (s *Store) GetAll() []*Item {
	s.mu.RLock()
	defer s.mu.RUnlock()

	items := make([]*Item, 0, len(s.items))
	for _, item := range s.items {
		items = append(items, item)
	}
	return items
}

func (s *Store) Get(id int) (*Item, bool) {
	s.mu.RLock()
	defer s.mu.RUnlock()

	item, ok := s.items[id]
	return item, ok
}

func (s *Store) Create(name string) (*Item, error) {
	if err := validateItemName(name); err != nil {
		return nil, err
	}

	s.mu.Lock()
	defer s.mu.Unlock()

	if len(s.items) >= s.maxItems {
		return nil, errStoreFull
	}

	item := &Item{
		ID:        s.nextID,
		Name:      name,
		Completed: false,
		CreatedAt: time.Now(),
	}
	s.items[s.nextID] = item
	s.nextID++
	return item, nil
}

func (s *Store) Update(id int, name *string, completed *bool) (*Item, bool, error) {
	if name != nil {
		if err := validateItemName(*name); err != nil {
			return nil, false, err
		}
	}

	s.mu.Lock()
	defer s.mu.Unlock()

	item, ok := s.items[id]
	if !ok {
		return nil, false, nil
	}

	if name != nil {
		item.Name = *name
	}
	if completed != nil {
		item.Completed = *completed
	}
	return item, true, nil
}

func (s *Store) Delete(id int) bool {
	s.mu.Lock()
	defer s.mu.Unlock()

	_, ok := s.items[id]
	if ok {
		delete(s.items, id)
	}
	return ok
}

func validateItemName(name string) error {
	if name == "" {
		return errItemNameRequired
	}
	if utf8.RuneCountInString(name) > maxItemNameLength {
		return errItemNameTooLong
	}
	return nil
}

func decodeJSONBody(w http.ResponseWriter, r *http.Request, dst any) bool {
	r.Body = http.MaxBytesReader(w, r.Body, maxJSONRequestBodyBytes)
	defer r.Body.Close()

	decoder := json.NewDecoder(r.Body)
	if err := decoder.Decode(dst); err != nil {
		writeJSONDecodeError(w, err)
		return false
	}

	if err := decoder.Decode(&struct{}{}); err != io.EOF {
		writeJSONDecodeError(w, err)
		return false
	}

	return true
}

func writeJSONResponse(w http.ResponseWriter, v any) {
	if err := json.NewEncoder(w).Encode(v); err != nil {
		log.Printf("write JSON response: %v", err)
	}
}

func writeJSONDecodeError(w http.ResponseWriter, err error) {
	var maxBytesErr *http.MaxBytesError
	if errors.As(err, &maxBytesErr) {
		http.Error(w, "Request body too large", http.StatusRequestEntityTooLarge)
		return
	}

	http.Error(w, "Invalid request body", http.StatusBadRequest)
}

func writeStoreError(w http.ResponseWriter, err error) {
	switch {
	case errors.Is(err, errItemNameRequired):
		http.Error(w, "Name is required", http.StatusBadRequest)
	case errors.Is(err, errItemNameTooLong):
		http.Error(w, "Name is too long", http.StatusBadRequest)
	case errors.Is(err, errStoreFull):
		http.Error(w, "Item limit reached", http.StatusConflict)
	default:
		http.Error(w, "Unable to store item", http.StatusInternalServerError)
	}
}

func seedStore(store *Store) error {
	for _, name := range []string{"Learn Go", "Build APIs", "Deploy with Aspire"} {
		if _, err := store.Create(name); err != nil {
			return err
		}
	}
	return nil
}

func main() {
	store := NewStore(maxStoredItems)

	// Add some initial data
	if err := seedStore(store); err != nil {
		log.Fatal(err)
	}

	r := chi.NewRouter()
	r.Use(middleware.Logger)
	r.Use(middleware.Recoverer)
	r.Use(middleware.RequestID)

	r.Get("/", func(w http.ResponseWriter, r *http.Request) {
		writeJSONResponse(w, map[string]string{
			"message": "Go API with in-memory storage",
			"version": "1.0.0",
		})
	})

	r.Get("/health", func(w http.ResponseWriter, r *http.Request) {
		w.WriteHeader(http.StatusOK)
		writeJSONResponse(w, map[string]string{"status": "healthy"})
	})

	r.Get("/items", func(w http.ResponseWriter, r *http.Request) {
		items := store.GetAll()
		w.Header().Set("Content-Type", "application/json")
		writeJSONResponse(w, items)
	})

	r.Get("/items/{id}", func(w http.ResponseWriter, r *http.Request) {
		id, err := strconv.Atoi(chi.URLParam(r, "id"))
		if err != nil {
			http.Error(w, "Invalid ID", http.StatusBadRequest)
			return
		}

		item, ok := store.Get(id)
		if !ok {
			http.Error(w, "Item not found", http.StatusNotFound)
			return
		}

		w.Header().Set("Content-Type", "application/json")
		writeJSONResponse(w, item)
	})

	r.Post("/items", func(w http.ResponseWriter, r *http.Request) {
		var req struct {
			Name string `json:"name"`
		}

		if !decodeJSONBody(w, r, &req) {
			return
		}

		item, err := store.Create(req.Name)
		if err != nil {
			writeStoreError(w, err)
			return
		}

		w.Header().Set("Content-Type", "application/json")
		w.WriteHeader(http.StatusCreated)
		writeJSONResponse(w, item)
	})

	r.Put("/items/{id}", func(w http.ResponseWriter, r *http.Request) {
		id, err := strconv.Atoi(chi.URLParam(r, "id"))
		if err != nil {
			http.Error(w, "Invalid ID", http.StatusBadRequest)
			return
		}

		var req struct {
			Name      *string `json:"name"`
			Completed *bool   `json:"completed"`
		}

		if !decodeJSONBody(w, r, &req) {
			return
		}

		item, ok, err := store.Update(id, req.Name, req.Completed)
		if err != nil {
			writeStoreError(w, err)
			return
		}
		if !ok {
			http.Error(w, "Item not found", http.StatusNotFound)
			return
		}

		w.Header().Set("Content-Type", "application/json")
		writeJSONResponse(w, item)
	})

	r.Delete("/items/{id}", func(w http.ResponseWriter, r *http.Request) {
		id, err := strconv.Atoi(chi.URLParam(r, "id"))
		if err != nil {
			http.Error(w, "Invalid ID", http.StatusBadRequest)
			return
		}

		ok := store.Delete(id)
		if !ok {
			http.Error(w, "Item not found", http.StatusNotFound)
			return
		}

		w.WriteHeader(http.StatusNoContent)
	})

	port := os.Getenv("PORT")
	if port == "" {
		port = "8080"
	}
	host := os.Getenv("HOST")
	if host == "" {
		host = defaultBindHost
	}

	addr := net.JoinHostPort(host, port)
	server := &http.Server{
		Addr:              addr,
		Handler:           r,
		ReadTimeout:       5 * time.Second,
		ReadHeaderTimeout: 2 * time.Second,
		WriteTimeout:      10 * time.Second,
		IdleTimeout:       60 * time.Second,
	}

	ctx, stop := signal.NotifyContext(context.Background(), os.Interrupt, syscall.SIGTERM)
	defer stop()

	serverErrs := make(chan error, 1)
	go func() {
		log.Printf("Starting server on %s", addr)
		if err := server.ListenAndServe(); err != nil && !errors.Is(err, http.ErrServerClosed) {
			serverErrs <- err
		}
		close(serverErrs)
	}()

	select {
	case err, ok := <-serverErrs:
		if ok && err != nil {
			log.Fatalf("server error: %v", err)
		}
	case <-ctx.Done():
		log.Println("Shutdown signal received, draining in-flight requests...")
	}

	shutdownCtx, cancel := context.WithTimeout(context.Background(), 10*time.Second)
	defer cancel()
	if err := server.Shutdown(shutdownCtx); err != nil {
		log.Printf("server shutdown: %v", err)
	}
}
