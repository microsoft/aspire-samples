/** Validation rules and the sample notes behind the "Seed sample notes" dashboard command. */
export const ID_PATTERN = /^[a-z0-9-]{1,40}$/;
export const ID_RULE = 'Use 1-40 lowercase letters, digits or hyphens for the note ID.';
export const TEXT_RULE = 'Supply text containing 1-1000 characters, not just whitespace.';

// The walkthrough's 'welcome' note is never touched by seeding.
export const SAMPLE_NOTES = [
    { id: 'standup', text: 'Standup at 09:30: Priya demos the Redis REPL, Marco is blocked on the staging certificate.' },
    { id: 'release-checklist', text: 'Release 2.4: tag main, run the smoke suite, publish the changelog and post in #releases.' },
    { id: 'oncall-handoff', text: 'On-call handoff: the checkout p95 latency alert stays muted until the cache warm-up fix ships.' },
    { id: 'design-review', text: 'Design review on Thursday at 14:00: note sharing, offline sync and the 1,000-character limit.' },
    { id: 'retro-actions', text: 'Retro actions: add Redis health to the status page and document the Slumber keyboard shortcuts.' },
] as const;
