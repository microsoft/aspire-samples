package main

import (
	"errors"
	"fmt"
	"hash/fnv"
	"maps"
	"regexp"
	"slices"
	"sort"
	"strings"
	"sync"
	"time"
)

// Flag is a feature flag: a set of named variants plus the rules that decide which variant a shopper gets.
type Flag struct {
	Key            string          `json:"key"`
	Description    string          `json:"description"`
	Enabled        bool            `json:"enabled"`
	Variants       map[string]any  `json:"variants"`
	DefaultVariant string          `json:"defaultVariant"`
	OffVariant     string          `json:"offVariant"`
	Rules          []TargetingRule `json:"rules"`
	Rollout        []Allocation    `json:"rollout"`
	Version        int             `json:"version"`
	UpdatedAt      time.Time       `json:"updatedAt"`
}

// TargetingRule serves Variant when the context attribute matches one of Values. The first matching rule wins.
type TargetingRule struct {
	Attribute string   `json:"attribute"`
	Values    []string `json:"values"`
	Variant   string   `json:"variant"`
}

// Allocation is one slice of a percentage rollout. A flag's allocations add up to 100.
type Allocation struct {
	Variant string `json:"variant"`
	Weight  int    `json:"weight"`
}

// FlagPatch is a partial update; nil fields are left unchanged.
type FlagPatch struct {
	Description    *string          `json:"description"`
	Enabled        *bool            `json:"enabled"`
	DefaultVariant *string          `json:"defaultVariant"`
	OffVariant     *string          `json:"offVariant"`
	Rules          *[]TargetingRule `json:"rules"`
	Rollout        *[]Allocation    `json:"rollout"`
}

// EvaluationContext describes who a flag is evaluated for, in OpenFeature terms.
type EvaluationContext struct {
	TargetingKey string            `json:"targetingKey"`
	Attributes   map[string]string `json:"attributes"`
}

// Evaluation is the resolved value of one flag for one context.
type Evaluation struct {
	FlagKey     string `json:"flagKey"`
	Value       any    `json:"value"`
	Variant     string `json:"variant,omitempty"`
	Reason      string `json:"reason"`
	ErrorCode   string `json:"errorCode,omitempty"`
	FlagVersion int    `json:"flagVersion,omitempty"`
}

// OpenFeature resolution reasons.
const (
	ReasonStatic         = "STATIC"
	ReasonDefault        = "DEFAULT"
	ReasonTargetingMatch = "TARGETING_MATCH"
	ReasonSplit          = "SPLIT"
	ReasonDisabled       = "DISABLED"
	ReasonError          = "ERROR"

	ErrorFlagNotFound = "FLAG_NOT_FOUND"
)

// rolloutStages are the percentages a progressive rollout moves through.
var rolloutStages = []int{5, 10, 25, 50, 100}

var (
	ErrFlagNotFound    = errors.New("flag not found")
	ErrFlagExists      = errors.New("flag already exists")
	ErrNotProgressive  = errors.New("flag doesn't have a two-variant percentage rollout")
	ErrFullyRolledOut  = errors.New("rollout is already at 100%")
	flagKeyPattern     = regexp.MustCompile(`^[a-z0-9]+([.-][a-z0-9]+)*$`)
	maxFlagKeyLength   = 64
	maxDescriptionSize = 280
)

// Evaluate resolves the flag for ctx: disabled flags serve the off variant, then targeting rules, then the
// percentage rollout (sticky per targeting key), then the default variant.
func (f *Flag) Evaluate(ctx EvaluationContext) Evaluation {
	if !f.Enabled {
		return f.result(f.OffVariant, ReasonDisabled)
	}
	for _, rule := range f.Rules {
		if value, ok := ctx.Attributes[rule.Attribute]; ok && slices.Contains(rule.Values, value) {
			return f.result(rule.Variant, ReasonTargetingMatch)
		}
	}
	if len(f.Rollout) > 0 {
		bucket := rolloutBucket(f.Key, ctx.TargetingKey)
		cumulative := 0
		for _, allocation := range f.Rollout {
			cumulative += allocation.Weight
			if bucket < cumulative {
				return f.result(allocation.Variant, ReasonSplit)
			}
		}
	}
	if len(f.Rules) > 0 {
		return f.result(f.DefaultVariant, ReasonDefault)
	}
	return f.result(f.DefaultVariant, ReasonStatic)
}

func (f *Flag) result(variant, reason string) Evaluation {
	return Evaluation{FlagKey: f.Key, Value: f.Variants[variant], Variant: variant, Reason: reason, FlagVersion: f.Version}
}

// RolloutPercentage is the share of rollout traffic that gets a non-default variant (0 when disabled or not rolled out).
func (f *Flag) RolloutPercentage() int {
	if !f.Enabled {
		return 0
	}
	percentage := 0
	for _, allocation := range f.Rollout {
		if allocation.Variant != f.DefaultVariant {
			percentage += allocation.Weight
		}
	}
	return percentage
}

// rolloutBucket hashes the flag and targeting key into 0-99 so a shopper keeps the same variant across requests.
func rolloutBucket(flagKey, targetingKey string) int {
	h := fnv.New32a()
	_, _ = h.Write([]byte(flagKey + "/" + targetingKey))
	return int(h.Sum32() % 100)
}

// Validate returns field errors in the shape of an RFC 9457 validation problem.
func (f *Flag) Validate() map[string][]string {
	errs := map[string][]string{}
	add := func(field, format string, args ...any) {
		errs[field] = append(errs[field], fmt.Sprintf(format, args...))
	}

	switch {
	case f.Key == "":
		add("key", "The key is required.")
	case len(f.Key) > maxFlagKeyLength:
		add("key", "The key must be at most %d characters.", maxFlagKeyLength)
	case !flagKeyPattern.MatchString(f.Key):
		add("key", "Use lowercase letters, digits, dots, and dashes, for example 'checkout.express-pay'.")
	}
	if len(f.Description) > maxDescriptionSize {
		add("description", "The description must be at most %d characters.", maxDescriptionSize)
	}
	if len(f.Variants) == 0 {
		add("variants", "Define at least one variant.")
	}
	for _, name := range slices.Sorted(maps.Keys(f.Variants)) {
		switch f.Variants[name].(type) {
		case bool, string, float64:
		default:
			add("variants."+name, "Variant values must be a boolean, string, or number.")
		}
	}
	checkVariant := func(field, variant string) {
		if _, ok := f.Variants[variant]; !ok {
			add(field, "'%s' isn't one of the flag's variants.", variant)
		}
	}
	checkVariant("defaultVariant", f.DefaultVariant)
	checkVariant("offVariant", f.OffVariant)
	for i, rule := range f.Rules {
		field := fmt.Sprintf("rules[%d]", i)
		if strings.TrimSpace(rule.Attribute) == "" {
			add(field+".attribute", "The attribute is required.")
		}
		if len(rule.Values) == 0 {
			add(field+".values", "List at least one value to match.")
		}
		checkVariant(field+".variant", rule.Variant)
	}
	if len(f.Rollout) > 0 {
		total := 0
		seen := map[string]bool{}
		for i, allocation := range f.Rollout {
			field := fmt.Sprintf("rollout[%d]", i)
			checkVariant(field+".variant", allocation.Variant)
			if seen[allocation.Variant] {
				add(field+".variant", "Each variant can appear only once in a rollout.")
			}
			seen[allocation.Variant] = true
			if allocation.Weight < 0 || allocation.Weight > 100 {
				add(field+".weight", "Weights must be between 0 and 100.")
			}
			total += allocation.Weight
		}
		if total != 100 {
			add("rollout", "Rollout weights must add up to 100 (got %d).", total)
		}
	}
	return errs
}

// FlagStore is an in-memory, concurrency-safe flag catalog.
type FlagStore struct {
	mu    sync.RWMutex
	flags map[string]*Flag
	now   func() time.Time
}

func NewFlagStore(seed []Flag) *FlagStore {
	s := &FlagStore{now: func() time.Time { return time.Now().UTC() }}
	s.Reset(seed)
	return s
}

// Reset replaces the catalog with the seed flags and returns how many were loaded.
func (s *FlagStore) Reset(seed []Flag) int {
	s.mu.Lock()
	defer s.mu.Unlock()
	s.flags = make(map[string]*Flag, len(seed))
	for _, f := range seed {
		f = f.clone()
		f.Version = 1
		f.UpdatedAt = s.now()
		s.flags[f.Key] = &f
	}
	return len(s.flags)
}

func (s *FlagStore) List(enabled *bool) []Flag {
	s.mu.RLock()
	defer s.mu.RUnlock()
	flags := make([]Flag, 0, len(s.flags))
	for _, f := range s.flags {
		if enabled == nil || f.Enabled == *enabled {
			flags = append(flags, f.clone())
		}
	}
	sort.Slice(flags, func(i, j int) bool { return flags[i].Key < flags[j].Key })
	return flags
}

func (s *FlagStore) Get(key string) (Flag, bool) {
	s.mu.RLock()
	defer s.mu.RUnlock()
	f, ok := s.flags[key]
	if !ok {
		return Flag{}, false
	}
	return f.clone(), true
}

func (s *FlagStore) Create(f Flag) (Flag, error) {
	s.mu.Lock()
	defer s.mu.Unlock()
	if _, exists := s.flags[f.Key]; exists {
		return Flag{}, ErrFlagExists
	}
	f = f.clone()
	f.Version = 1
	f.UpdatedAt = s.now()
	s.flags[f.Key] = &f
	return f.clone(), nil
}

// Update applies the patch, validates the result, and returns the flag before and after the change.
func (s *FlagStore) Update(key string, patch FlagPatch) (before, after Flag, problems map[string][]string, err error) {
	s.mu.Lock()
	defer s.mu.Unlock()
	current, ok := s.flags[key]
	if !ok {
		return Flag{}, Flag{}, nil, ErrFlagNotFound
	}
	before = current.clone()
	after = current.clone()
	if patch.Description != nil {
		after.Description = *patch.Description
	}
	if patch.Enabled != nil {
		after.Enabled = *patch.Enabled
	}
	if patch.DefaultVariant != nil {
		after.DefaultVariant = *patch.DefaultVariant
	}
	if patch.OffVariant != nil {
		after.OffVariant = *patch.OffVariant
	}
	if patch.Rules != nil {
		after.Rules = slices.Clone(*patch.Rules)
	}
	if patch.Rollout != nil {
		after.Rollout = slices.Clone(*patch.Rollout)
	}
	if problems = after.Validate(); len(problems) > 0 {
		return before, Flag{}, problems, nil
	}
	after.Version++
	after.UpdatedAt = s.now()
	stored := after.clone()
	s.flags[key] = &stored
	return before, after, nil, nil
}

// AdvanceRollout moves a two-variant rollout's non-default variant to the next stage in rolloutStages.
func (s *FlagStore) AdvanceRollout(key string) (before, after Flag, err error) {
	s.mu.Lock()
	defer s.mu.Unlock()
	current, ok := s.flags[key]
	if !ok {
		return Flag{}, Flag{}, ErrFlagNotFound
	}
	if len(current.Rollout) != 2 || !slices.ContainsFunc(current.Rollout, func(a Allocation) bool { return a.Variant == current.DefaultVariant }) {
		return Flag{}, Flag{}, ErrNotProgressive
	}
	before = current.clone()
	percentage := 100 - weightOf(before.Rollout, before.DefaultVariant)
	next := slices.IndexFunc(rolloutStages, func(stage int) bool { return stage > percentage })
	if next < 0 {
		return Flag{}, Flag{}, ErrFullyRolledOut
	}
	after = current.clone()
	for i := range after.Rollout {
		if after.Rollout[i].Variant == after.DefaultVariant {
			after.Rollout[i].Weight = 100 - rolloutStages[next]
		} else {
			after.Rollout[i].Weight = rolloutStages[next]
		}
	}
	after.Version++
	after.UpdatedAt = s.now()
	stored := after.clone()
	s.flags[key] = &stored
	return before, after, nil
}

func (s *FlagStore) Delete(key string) bool {
	s.mu.Lock()
	defer s.mu.Unlock()
	if _, ok := s.flags[key]; !ok {
		return false
	}
	delete(s.flags, key)
	return true
}

// RolloutPercentages reports each rolled-out flag's percentage for the rollout gauge.
func (s *FlagStore) RolloutPercentages() map[string]int {
	s.mu.RLock()
	defer s.mu.RUnlock()
	percentages := map[string]int{}
	for key, f := range s.flags {
		if len(f.Rollout) > 0 {
			percentages[key] = f.RolloutPercentage()
		}
	}
	return percentages
}

func weightOf(allocations []Allocation, variant string) int {
	for _, a := range allocations {
		if a.Variant == variant {
			return a.Weight
		}
	}
	return 0
}

func (f *Flag) clone() Flag {
	c := *f
	c.Variants = maps.Clone(f.Variants)
	c.Rules = make([]TargetingRule, len(f.Rules))
	for i, rule := range f.Rules {
		c.Rules[i] = TargetingRule{Attribute: rule.Attribute, Values: slices.Clone(rule.Values), Variant: rule.Variant}
	}
	c.Rollout = slices.Clone(f.Rollout)
	if c.Rollout == nil {
		c.Rollout = []Allocation{}
	}
	return c
}
