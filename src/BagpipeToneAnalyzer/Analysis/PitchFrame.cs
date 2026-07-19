namespace BagpipeToneAnalyzer.Analysis;

/// <summary>One analysis window's pitch estimate. FrequencyHz is null when no clear pitch was found.</summary>
public readonly record struct PitchFrame(double TimeSeconds, double? FrequencyHz);
