namespace FSCM.Modules.Logistics.Domain.Transit;

/// <summary>
/// ETA(from, to, t) = next dispatch after (t + preparation + buffer) + transit(km).
/// Dispatch slots are fixed times per day (e.g. 07:00 morning run).
/// </summary>
public sealed class EtaCalculator(IReadOnlyList<TimeOnly> dispatchSlots, TimeSpan preparation, TimeSpan buffer)
{
    public DateTime NextDispatch(DateTime readyFrom)
    {
        var ready = readyFrom + preparation + buffer;
        for (var day = DateOnly.FromDateTime(ready); ; day = day.AddDays(1))
            foreach (var slot in dispatchSlots.OrderBy(s => s))
            {
                var at = day.ToDateTime(slot);
                if (at >= ready) return at;
            }
    }
    public DateOnly Eta(DateTime readyFrom, TransitQuote quote) => DateOnly.FromDateTime(NextDispatch(readyFrom)).AddDays(quote.TransitDays);
}
