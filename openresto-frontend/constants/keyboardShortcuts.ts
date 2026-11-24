export type ShortcutScope = "admin" | "user";

export interface ShortcutEntry {
  keys: string;
  description: string;
  descriptionKey: `common.keyboardShortcuts.descriptions.${
    | "dashboard"
    | "bookings"
    | "locations"
    | "brand"
    | "search"
    | "create"
    | "down"
    | "up"
    | "open"
    | "extend"
    | "closeDialog"
    | "toggleHelp"
    | "lookup"}`;
}

// Admin scope. "/" navigates to the bookings list and focuses its lookup input:
// that bar is the one booking search left, so the shortcut goes to it from
// anywhere in the admin rather than to a sidebar copy of the same control.
export const ADMIN_SHORTCUTS: ShortcutEntry[] = [
  { keys: "g d", description: "Go to Dashboard", descriptionKey: "common.keyboardShortcuts.descriptions.dashboard" },
  { keys: "g b", description: "Go to Bookings", descriptionKey: "common.keyboardShortcuts.descriptions.bookings" },
  { keys: "g l", description: "Go to Locations", descriptionKey: "common.keyboardShortcuts.descriptions.locations" },
  { keys: "g s", description: "Go to Brand settings", descriptionKey: "common.keyboardShortcuts.descriptions.brand" },
  { keys: "/", description: "Search bookings by name, email or reference", descriptionKey: "common.keyboardShortcuts.descriptions.search" },
  { keys: "c", description: "Create a new booking", descriptionKey: "common.keyboardShortcuts.descriptions.create" },
  { keys: "j / ↓", description: "Move selection down in the bookings list", descriptionKey: "common.keyboardShortcuts.descriptions.down" },
  { keys: "k / ↑", description: "Move selection up in the bookings list", descriptionKey: "common.keyboardShortcuts.descriptions.up" },
  { keys: "Enter", description: "Open the selected booking", descriptionKey: "common.keyboardShortcuts.descriptions.open" },
  { keys: "e", description: "Open the selected booking's extend controls", descriptionKey: "common.keyboardShortcuts.descriptions.extend" },
  { keys: "Esc", description: "Close any open dialog", descriptionKey: "common.keyboardShortcuts.descriptions.closeDialog" },
  { keys: "?", description: "Toggle this help overlay", descriptionKey: "common.keyboardShortcuts.descriptions.toggleHelp" },
];

// End-user scope. Per the issue #140 maintainer decision: no home-page search
// input exists and none is built as part of this feature, so "/" is
// intentionally absent here — do not add it.
export const USER_SHORTCUTS: ShortcutEntry[] = [
  { keys: "l", description: "Jump to the Find My Booking lookup", descriptionKey: "common.keyboardShortcuts.descriptions.lookup" },
  { keys: "Esc", description: "Close any open dialog", descriptionKey: "common.keyboardShortcuts.descriptions.closeDialog" },
  { keys: "?", description: "Toggle this help overlay", descriptionKey: "common.keyboardShortcuts.descriptions.toggleHelp" },
];

export const SHORTCUTS_BY_SCOPE: Record<ShortcutScope, ShortcutEntry[]> = {
  admin: ADMIN_SHORTCUTS,
  user: USER_SHORTCUTS,
};
