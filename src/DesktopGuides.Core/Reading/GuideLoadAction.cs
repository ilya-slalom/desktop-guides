namespace DesktopGuides.Core.Reading;

// The one thing the Reader offers to do about an error, for every format.
// New members go at the end, so existing values keep their numbers.
public enum GuideLoadAction { None, GetRuntime, Reopen, Remove }
