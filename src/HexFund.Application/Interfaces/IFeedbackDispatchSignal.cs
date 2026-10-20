namespace HexFund.Application.Interfaces;

/// <summary>
/// Wakes the dispatcher as soon as a report is stored, instead of waiting for its next poll. On a
/// host without Always On this is what gets a report out while the app is awake.
/// </summary>
public interface IFeedbackDispatchSignal
{
    void Notify();
}
