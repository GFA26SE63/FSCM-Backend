namespace FSCM.SharedKernel.Time;

public static class DateMath
{
    public static int DaysBetween(DateOnly from, DateOnly to) => to.DayNumber - from.DayNumber;
}
