package main

import (
	"cmp"
	"errors"
	"maps"
	"slices"
	"strings"
	"sync"
	"time"
	"unicode/utf8"
)

// Priority is the dispatch priority of a work order.
type Priority string

const (
	PriorityLow    Priority = "low"
	PriorityNormal Priority = "normal"
	PriorityHigh   Priority = "high"
	PriorityUrgent Priority = "urgent"
)

var priorityRank = map[Priority]int{PriorityLow: 0, PriorityNormal: 1, PriorityHigh: 2, PriorityUrgent: 3}

func (p Priority) valid() bool {
	_, ok := priorityRank[p]
	return ok
}

// Status is where a work order is in its lifecycle. Completed and cancelled work orders are closed.
type Status string

const (
	StatusOpen       Status = "open"
	StatusInProgress Status = "in_progress"
	StatusCompleted  Status = "completed"
	StatusCancelled  Status = "cancelled"
)

func (s Status) valid() bool {
	return s == StatusOpen || s == StatusInProgress || s == StatusCompleted || s == StatusCancelled
}

func (s Status) closed() bool { return s == StatusCompleted || s == StatusCancelled }

const (
	dateLayout        = time.DateOnly
	maxTitleLength    = 200
	maxAssigneeLength = 100
)

// WorkOrder is a field-service job, such as a repair or an inspection at a customer site.
type WorkOrder struct {
	ID          int        `json:"id"`
	Title       string     `json:"title"`
	Priority    Priority   `json:"priority"`
	Status      Status     `json:"status"`
	Assignee    string     `json:"assignee,omitempty"`
	DueDate     string     `json:"dueDate,omitempty"`
	Overdue     bool       `json:"overdue"`
	ReportedAt  time.Time  `json:"reportedAt"`
	CreatedAt   time.Time  `json:"createdAt"`
	UpdatedAt   time.Time  `json:"updatedAt"`
	CompletedAt *time.Time `json:"completedAt,omitempty"`
}

// overdue reports whether an open work order has passed its due date.
func (o WorkOrder) overdue(now time.Time) bool {
	if o.Status.closed() || o.DueDate == "" {
		return false
	}
	due, err := time.ParseInLocation(dateLayout, o.DueDate, now.Location())
	return err == nil && now.After(due.AddDate(0, 0, 1))
}

// CreateWorkOrderRequest is the body of POST /work-orders.
type CreateWorkOrderRequest struct {
	Title      string     `json:"title"`
	Priority   Priority   `json:"priority,omitempty"`
	Assignee   string     `json:"assignee,omitempty"`
	DueDate    string     `json:"dueDate,omitempty"`
	ReportedAt *time.Time `json:"reportedAt,omitempty"`
}

// UpdateWorkOrderRequest is the body of PATCH /work-orders/{id}. Omitted fields are left unchanged.
type UpdateWorkOrderRequest struct {
	Title    *string   `json:"title,omitempty"`
	Priority *Priority `json:"priority,omitempty"`
	Assignee *string   `json:"assignee,omitempty"`
	Status   *Status   `json:"status,omitempty"`
	DueDate  *string   `json:"dueDate,omitempty"`
}

// Filter narrows GET /work-orders. Empty fields match everything.
type Filter struct {
	Status   Status
	Priority Priority
	Assignee string
}

var (
	errNotFound  = errors.New("work order not found")
	errClosed    = errors.New("work order is closed")
	errStoreFull = errors.New("work order backlog is at capacity")
)

// ValidationError lists every invalid field of a request, keyed by JSON field name.
type ValidationError struct {
	Errors map[string][]string
}

func (e *ValidationError) Error() string {
	return "invalid work order: " + strings.Join(slices.Sorted(maps.Keys(e.Errors)), ", ")
}

func (e *ValidationError) add(field, message string) {
	if e.Errors == nil {
		e.Errors = map[string][]string{}
	}
	e.Errors[field] = append(e.Errors[field], message)
}

func (e *ValidationError) orNil() error {
	if len(e.Errors) == 0 {
		return nil
	}
	return e
}

func validateTitle(v *ValidationError, title string) {
	switch {
	case strings.TrimSpace(title) == "":
		v.add("title", "A title is required.")
	case utf8.RuneCountInString(title) > maxTitleLength:
		v.add("title", "The title must be 200 characters or fewer.")
	}
}

func validatePriority(v *ValidationError, p Priority) {
	if !p.valid() {
		v.add("priority", "The priority must be one of low, normal, high, or urgent.")
	}
}

func validateAssignee(v *ValidationError, assignee string) {
	if utf8.RuneCountInString(assignee) > maxAssigneeLength {
		v.add("assignee", "The assignee must be 100 characters or fewer.")
	}
}

func validateDueDate(v *ValidationError, due string) {
	if due == "" {
		return
	}
	if _, err := time.Parse(dateLayout, due); err != nil {
		v.add("dueDate", "The due date must be a calendar date such as 2026-11-30.")
	}
}

func (r *CreateWorkOrderRequest) validate(now time.Time) error {
	v := &ValidationError{}
	validateTitle(v, r.Title)
	if r.Priority != "" {
		validatePriority(v, r.Priority)
	}
	validateAssignee(v, r.Assignee)
	validateDueDate(v, r.DueDate)
	if r.ReportedAt != nil && r.ReportedAt.After(now.Add(time.Minute)) {
		v.add("reportedAt", "The reported time can't be in the future.")
	}
	return v.orNil()
}

func (r *UpdateWorkOrderRequest) validate() error {
	v := &ValidationError{}
	if r.Title != nil {
		validateTitle(v, *r.Title)
	}
	if r.Priority != nil {
		validatePriority(v, *r.Priority)
	}
	if r.Assignee != nil {
		validateAssignee(v, *r.Assignee)
	}
	if r.Status != nil && !r.Status.valid() {
		v.add("status", "The status must be one of open, in_progress, completed, or cancelled.")
	}
	if r.DueDate != nil {
		validateDueDate(v, *r.DueDate)
	}
	return v.orNil()
}

// apply merges the update into o and enforces the dispatch rules: technicians must be assigned before
// work starts or completes, and closed work orders can't change.
func (r *UpdateWorkOrderRequest) apply(o *WorkOrder, now time.Time) error {
	if o.Status.closed() {
		return errClosed
	}
	if r.Title != nil {
		o.Title = strings.TrimSpace(*r.Title)
	}
	if r.Priority != nil {
		o.Priority = *r.Priority
	}
	if r.Assignee != nil {
		o.Assignee = strings.TrimSpace(*r.Assignee)
	}
	if r.DueDate != nil {
		o.DueDate = *r.DueDate
	}
	if r.Status != nil {
		o.Status = *r.Status
	}
	if o.Assignee == "" && (o.Status == StatusInProgress || o.Status == StatusCompleted) {
		v := &ValidationError{}
		v.add("assignee", "Assign a technician before starting or completing a work order.")
		return v
	}
	if o.Status == StatusCompleted {
		o.CompletedAt = &now
	}
	o.UpdatedAt = now
	return nil
}

// Store is a thread-safe, in-memory work-order repository. It returns copies so callers never share
// state with the store.
type Store struct {
	mu     sync.RWMutex
	orders map[int]*WorkOrder
	nextID int
	max    int
}

func NewStore(maxOrders int) *Store {
	return &Store{orders: map[int]*WorkOrder{}, nextID: 1, max: maxOrders}
}

// List returns matching work orders as a dispatch queue: highest priority first, then earliest due date.
func (s *Store) List(f Filter, now time.Time) []WorkOrder {
	s.mu.RLock()
	defer s.mu.RUnlock()

	result := []WorkOrder{}
	for _, o := range s.orders {
		if (f.Status == "" || o.Status == f.Status) &&
			(f.Priority == "" || o.Priority == f.Priority) &&
			(f.Assignee == "" || strings.EqualFold(o.Assignee, f.Assignee)) {
			result = append(result, view(o, now))
		}
	}
	slices.SortFunc(result, func(a, b WorkOrder) int {
		return cmp.Or(
			cmp.Compare(priorityRank[b.Priority], priorityRank[a.Priority]),
			cmp.Compare(dueSortKey(a), dueSortKey(b)),
			cmp.Compare(a.ID, b.ID))
	})
	return result
}

func dueSortKey(o WorkOrder) string {
	if o.DueDate == "" {
		return "9999-12-31"
	}
	return o.DueDate
}

func (s *Store) Get(id int, now time.Time) (WorkOrder, error) {
	s.mu.RLock()
	defer s.mu.RUnlock()

	o, ok := s.orders[id]
	if !ok {
		return WorkOrder{}, errNotFound
	}
	return view(o, now), nil
}

func (s *Store) Create(o WorkOrder, now time.Time) (WorkOrder, error) {
	s.mu.Lock()
	defer s.mu.Unlock()

	if len(s.orders) >= s.max {
		return WorkOrder{}, errStoreFull
	}
	o.ID = s.nextID
	s.nextID++
	s.orders[o.ID] = &o
	return view(&o, now), nil
}

// Update applies fn to a copy of the work order and saves it only if fn succeeds.
func (s *Store) Update(id int, now time.Time, fn func(*WorkOrder) error) (before, after WorkOrder, err error) {
	s.mu.Lock()
	defer s.mu.Unlock()

	o, ok := s.orders[id]
	if !ok {
		return WorkOrder{}, WorkOrder{}, errNotFound
	}
	before = view(o, now)
	updated := *o
	if err := fn(&updated); err != nil {
		return before, before, err
	}
	*o = updated
	return before, view(o, now), nil
}

func (s *Store) Delete(id int, now time.Time) (WorkOrder, error) {
	s.mu.Lock()
	defer s.mu.Unlock()

	o, ok := s.orders[id]
	if !ok {
		return WorkOrder{}, errNotFound
	}
	delete(s.orders, id)
	return view(o, now), nil
}

// Reset replaces the store's contents and returns the work orders that were removed.
func (s *Store) Reset(orders []WorkOrder, now time.Time) (removed, added []WorkOrder) {
	s.mu.Lock()
	defer s.mu.Unlock()

	for _, o := range s.orders {
		removed = append(removed, view(o, now))
	}
	s.orders = map[int]*WorkOrder{}
	s.nextID = 1
	for _, o := range orders {
		o.ID = s.nextID
		s.nextID++
		s.orders[o.ID] = &o
		added = append(added, view(&o, now))
	}
	return removed, added
}

func view(o *WorkOrder, now time.Time) WorkOrder {
	v := *o
	v.Overdue = v.overdue(now)
	return v
}
