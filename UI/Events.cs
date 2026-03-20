using Microsoft.Xna.Framework;

namespace SpawnAnalyzer.UI;

public abstract class SpawnAnalyzerUIEvent {}

public class NewPosSelectedEvent: SpawnAnalyzerUIEvent
{
    public Point? Position;

    public NewPosSelectedEvent(Point? position)
    {
        Position = position;
    }
}

public class NewSpawnAnalysisEvent: SpawnAnalyzerUIEvent
{
    public SpawnAnalysis? Analysis;

    public NewSpawnAnalysisEvent(SpawnAnalysis? analysis)
    {
        Analysis = analysis;
    }
}