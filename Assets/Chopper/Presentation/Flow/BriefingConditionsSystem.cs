using Chopper.Presentation.Bridge;
using Chopper.Simulation.Visuals;
using Unity.Entities;

namespace Chopper.Presentation.Flow
{
    /// <summary>
    /// Client: puts the match's time of day and weather (<see cref="MatchConditions"/>, from the server's map
    /// announcement) on the load screen's briefing, e.g. "NIGHT // RAIN". Only does work when they change.
    /// </summary>
    [WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation)]
    [UpdateInGroup(typeof(PresentationSystemGroup))]
    public partial class BriefingConditionsSystem : PresentationSystemBase
    {
        private int _shown = -1;
        private string _text = "";

        protected override void OnCreate()
        {
            base.OnCreate();
            RequireForUpdate<MatchConditions>();
        }

        protected override void OnUpdate()
        {
            if (!TryGetContext(out var context)) return;
            var conditions = SystemAPI.GetSingleton<MatchConditions>();
            var key = (int)conditions.Time << 8 | (int)conditions.Weather;
            // The briefing is new per transition, so it is written every frame (the flow ignores the same text).
            if (key != _shown)
            {
                _shown = key;
                _text = Text(conditions.Time, conditions.Weather);
            }
            context.SetBriefingConditions(_text);
        }

        /// <summary>"NIGHT // RAIN", "DAY // CLEAR SKY".</summary>
        public static string Text(TimeOfDay time, Weather weather)
        {
            var when = time switch
            {
                TimeOfDay.Morning => "MORNING",
                TimeOfDay.Day => "DAY",
                TimeOfDay.Afternoon => "AFTERNOON",
                _ => "NIGHT",
            };
            var sky = weather switch
            {
                Weather.Rain => "RAIN",
                Weather.Sandstorm => "SANDSTORM",
                Weather.Fog => "FOG",
                Weather.Snow => "SNOW",
                Weather.Ash => "ASHFALL",
                _ => "CLEAR SKY",
            };
            return when + " // " + sky;
        }
    }
}
