namespace GetIdeas.Worker.Models;

public sealed record IdeaAnalysis(
    bool IsBusinessIdea,
    string ProblemSummary,
    string PotentialSolution,
    int ConfidenceScore,
    string Reasoning);
