namespace Utilities
{
    public static class Speed
    {
        public static float MetersPerSecondToKilometersPerHour(float metersPerSecond) => metersPerSecond * 3.6f;
        public static float MetersPerSecondToMilesPerHour(float metersPerSecond) => metersPerSecond * 2.237f;
        public static float MetersPerSecondToKnots(float metersPerSecond) => metersPerSecond * 1.944f;
        public static float MetersPerSecondToFeetPerSecond(float metersPerSecond) => metersPerSecond * 3.281f;
        public static float MetersPerSecondToMach(float metersPerSecond) => metersPerSecond * 0.0029f;
        
    }
}