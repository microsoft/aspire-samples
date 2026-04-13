package main

import (
	"encoding/json"
	"log"
	"net/http"
	"os"
	"strconv"
	"sync"
	"time"

	"github.com/go-chi/chi/v5"
	"github.com/go-chi/chi/v5/middleware"
)

type Item struct {
	ID        int       `json:"id"`
	Name      string    `json:"name"`
	Completed bool      `json:"completed"`
	CreatedAt time.Time `json:"createdAt"`
}

type Store struct {
	mu    sync.RWMutex
	items map[int]*Item
	nextID int
}

func NewStore() *Store {
	return &Store{
		items: make(map[int]*Item),
		nextID: 1,
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

func (s *Store) Create(name string) *Item {
	s.mu.Lock()
	defer s.mu.Unlock()

	item := &Item{
		ID:        s.nextID,
		Name:      name,
		Completed: false,
		CreatedAt: time.Now(),
	}
	s.items[s.nextID] = item
	s.nextID++
	return item
}

func (s *Store) Update(id int, name *string, completed *bool) (*Item, bool) {
	s.mu.Lock()
	defer s.mu.Unlock()

	item, ok := s.items[id]
	if !ok {
		return nil, false
	}

	if name != nil {
		item.Name = *name
	}
	if completed != nil {
		item.Completed = *completed
	}
	return item, true
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

func main() {
	store := NewStore()

	// Add some initial data
	store.Create("Learn Go")
	store.Create("Build APIs")
	store.Create("Deploy with Aspire")

	r := chi.NewRouter()
	r.Use(middleware.Logger)
	r.Use(middleware.Recoverer)
	r.Use(middleware.RequestID)

	r.Get("/", func(w http.ResponseWriter, r *http.Request) {
		json.NewEncoder(w).Encode(map[string]string{
			"message": "Go API with in-memory storage",
			"version": "1.0.0",
		})
	})

	r.Get("/health", func(w http.ResponseWriter, r *http.Request) {
		w.WriteHeader(http.StatusOK)
		json.NewEncoder(w).Encode(map[string]string{"status": "healthy"})
	})

	r.Get("/items", func(w http.ResponseWriter, r *http.Request) {
		items := store.GetAll()
		w.Header().Set("Content-Type", "application/json")
		json.NewEncoder(w).Encode(items)
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
		json.NewEncoder(w).Encode(item)
	})

	r.Post("/items", func(w http.ResponseWriter, r *http.Request) {
		var req struct {
			Name string `json:"name"`
		}

		if err := json.NewDecoder(r.Body).Decode(&req); err != nil {
			http.Error(w, "Invalid request body", http.StatusBadRequest)
			return
		}

		if req.Name == "" {
			http.Error(w, "Name is required", http.StatusBadRequest)
			return
		}

		item := store.Create(req.Name)
		w.Header().Set("Content-Type", "application/json")
		w.WriteHeader(http.StatusCreated)
		json.NewEncoder(w).Encode(item)
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

		if err := json.NewDecoder(r.Body).Decode(&req); err != nil {
			http.Error(w, "Invalid request body", http.StatusBadRequest)
			return
		}

		item, ok := store.Update(id, req.Name, req.Completed)
		if !ok {
			http.Error(w, "Item not found", http.StatusNotFound)
			return
		}

		w.Header().Set("Content-Type", "application/json")
		json.NewEncoder(w).Encode(item)
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

	log.Printf("Starting server on port %s", port)
	if err := http.ListenAndServe(":"+port, r); err != nil {
		log.Fatal(err)
	}
}
