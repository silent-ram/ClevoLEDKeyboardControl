CREATE TABLE IF NOT EXISTS installations (
  install_id TEXT PRIMARY KEY,
  first_seen TEXT NOT NULL,
  last_seen TEXT NOT NULL,
  current_version TEXT NOT NULL
);

CREATE TABLE IF NOT EXISTS daily_activity (
  activity_date TEXT NOT NULL,
  install_id TEXT NOT NULL,
  PRIMARY KEY (activity_date, install_id)
);

CREATE INDEX IF NOT EXISTS idx_installations_last_seen
ON installations(last_seen);

CREATE INDEX IF NOT EXISTS idx_daily_activity_date
ON daily_activity(activity_date);
