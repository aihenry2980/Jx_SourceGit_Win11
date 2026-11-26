using System;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Threading;

namespace SourceGit.Views
{
    public class CommitTimeTextBlock : TextBlock
    {
        public static readonly DirectProperty<CommitTimeTextBlock, bool> ShowAsDateTimeProperty =
            AvaloniaProperty.RegisterDirect<CommitTimeTextBlock, bool>(
                nameof(ShowAsDateTime),
                static o => o.ShowAsDateTime,
                static (o, v) => o.ShowAsDateTime = v);

        public bool ShowAsDateTime
        {
            get => _showAsDateTime;
            set => SetAndRaise(ShowAsDateTimeProperty, ref _showAsDateTime, value);
        }

        public static readonly DirectProperty<CommitTimeTextBlock, bool> Use24HoursProperty =
            AvaloniaProperty.RegisterDirect<CommitTimeTextBlock, bool>(
                nameof(Use24Hours),
                static o => o.Use24Hours,
                static (o, v) => o.Use24Hours = v);

        public bool Use24Hours
        {
            get => _use24Hours;
            set => SetAndRaise(Use24HoursProperty, ref _use24Hours, value);
        }

        public static readonly StyledProperty<bool> UseAuthorTimeProperty =
            AvaloniaProperty.Register<CommitTimeTextBlock, bool>(nameof(UseAuthorTime));

        public bool UseAuthorTime
        {
            get => GetValue(UseAuthorTimeProperty);
            set => SetValue(UseAuthorTimeProperty, value);
        }

        public static readonly DirectProperty<CommitTimeTextBlock, int> DateTimeFormatProperty =
            AvaloniaProperty.RegisterDirect<CommitTimeTextBlock, int>(
                nameof(DateTimeFormat),
                static o => o.DateTimeFormat,
                static (o, v) => o.DateTimeFormat = v);

        public int DateTimeFormat
        {
            get => _dateTimeFormat;
            set => SetAndRaise(DateTimeFormatProperty, ref _dateTimeFormat, value);
        }

        public static readonly DirectProperty<CommitTimeTextBlock, ulong> TimestampProperty =
            AvaloniaProperty.RegisterDirect<CommitTimeTextBlock, ulong>(
                nameof(Timestamp),
                static o => o.Timestamp,
                static (o, v) => o.Timestamp = v);

        public ulong Timestamp
        {
            get => _timestamp;
            set => SetAndRaise(TimestampProperty, ref _timestamp, value);
        }

        protected override Type StyleKeyOverride => typeof(TextBlock);

        protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
        {
            base.OnPropertyChanged(change);

            if (change.Property == TimestampProperty)
            {
                RefreshDisplay();
            }
            else if (change.Property == ShowAsDateTimeProperty)
            {
                RefreshDisplay();
                HorizontalAlignment = ShowAsDateTime ? HorizontalAlignment.Left : HorizontalAlignment.Center;
            }
            else if (change.Property == DateTimeFormatProperty || change.Property == Use24HoursProperty || change.Property == UseAuthorTimeProperty)
            {
                RefreshDisplay();
            }
        }

        protected override void OnLoaded(RoutedEventArgs e)
        {
            base.OnLoaded(e);

            _refreshTimer = new DispatcherTimer();
            _refreshTimer.Interval = TimeSpan.FromSeconds(10);
            _refreshTimer.Tag = this;
            _refreshTimer.Tick += static (o, _) =>
            {
                if (o is DispatcherTimer { Tag: CommitTimeTextBlock textBlock })
                {
                    if (textBlock.ShowAsDateTime)
                        textBlock.UpdateRecency(DateTime.Now);
                    else
                        textBlock.RefreshDisplay();
                }
            };
            RefreshDisplay();
            // Absolute dates also need to change color when the local day rolls over.
            _refreshTimer.Start();
        }

        protected override void OnUnloaded(RoutedEventArgs e)
        {
            if (_refreshTimer != null)
            {
                _refreshTimer.Tag = null;
                _refreshTimer.Stop();
                _refreshTimer = null;
            }

            base.OnUnloaded(e);
        }

        protected override void OnDataContextChanged(EventArgs e)
        {
            base.OnDataContextChanged(e);
            RefreshDisplay();
        }

        private ulong GetTimestamp()
        {
            if (DataContext is not Models.Commit commit)
                return 0;

            var timestamp = UseAuthorTime ? commit.AuthorTime : commit.CommitterTime;
            return timestamp == 0 ? Timestamp : timestamp;
        }

        private void RefreshDisplay()
        {
            var now = DateTime.Now;
            var text = GetDisplayText(now);
            if (!string.Equals(text, Text, StringComparison.Ordinal))
                SetCurrentValue(TextProperty, text);
            UpdateRecency(now);
        }

        private void UpdateRecency(DateTime now)
        {
            var timestamp = GetTimestamp();
            var date = timestamp > 0 && timestamp <= s_maxTimestamp
                ? DateTime.UnixEpoch.AddSeconds(timestamp).ToLocalTime().Date
                : DateTime.MinValue;
            Classes.Set("time_today", date == now.Date);
            Classes.Set("time_yesterday", date == now.Date.AddDays(-1));
        }

        private string GetDisplayText(DateTime now)
        {
            if (DataContext is not Models.Commit)
                return string.Empty;

            var timestamp = GetTimestamp();
            if (timestamp > s_maxTimestamp)
                return string.Empty;
            if (ShowAsDateTime)
                return Models.DateTimeFormat.Format(timestamp);

            var localTime = DateTime.UnixEpoch.AddSeconds(timestamp).ToLocalTime();
            var span = now - localTime;
            if (span.TotalMinutes < 1)
                return App.Text("Period.JustNow");

            if (span.TotalHours < 1)
                return App.Text("Period.MinutesAgo", (int)span.TotalMinutes);

            if (span.TotalDays < 1)
            {
                var hours = (int)span.TotalHours;
                return hours == 1 ? App.Text("Period.HourAgo") : App.Text("Period.HoursAgo", hours);
            }

            var lastDay = now.AddDays(-1).Date;
            if (localTime >= lastDay)
                return App.Text("Period.Yesterday");

            if ((localTime.Year == now.Year && localTime.Month == now.Month) || span.TotalDays < 28)
            {
                var diffDay = now.Date - localTime.Date;
                return App.Text("Period.DaysAgo", (int)diffDay.TotalDays);
            }

            var lastMonth = now.AddMonths(-1).Date;
            if (localTime.Year == lastMonth.Year && localTime.Month == lastMonth.Month)
                return App.Text("Period.LastMonth");

            if (localTime.Year == now.Year || localTime > now.AddMonths(-11))
            {
                var diffMonth = (12 + now.Month - localTime.Month) % 12;
                return App.Text("Period.MonthsAgo", diffMonth);
            }

            var diffYear = now.Year - localTime.Year;
            if (diffYear == 1)
                return App.Text("Period.LastYear");

            return App.Text("Period.YearsAgo", diffYear);
        }

        private static readonly ulong s_maxTimestamp = (ulong)DateTimeOffset.MaxValue.ToUnixTimeSeconds();

        private bool _showAsDateTime = true;
        private bool _use24Hours = true;
        private int _dateTimeFormat = 0;
        private ulong _timestamp = 0;
        private DispatcherTimer _refreshTimer = null;
    }
}
