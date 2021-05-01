using System.Linq;

namespace subbuzz.Parser.Qualities
{
    public static class QualityFinder
    {
        public static Quality FindBySourceAndResolution(Source source, int resolution, Modifier modifer)
        {
            // Check for a perfect 3-way match
            var matchingQuality = Quality.All.SingleOrDefault(q => q.Source == source && q.Resolution == resolution && q.Modifier == modifer);

            if (matchingQuality != null)
            {
                return matchingQuality;
            }

            // Check for Source and Modifier Match for Qualities with Unknown Resolution
            var matchingQualitiesUnknownResolution = Quality.All.Where(q => q.Source == source && (q.Resolution == 0) && q.Modifier == modifer && q != Quality.Unknown);

            if (matchingQualitiesUnknownResolution.Any())
            {
                if (matchingQualitiesUnknownResolution.Count() == 1)
                {
                    return matchingQualitiesUnknownResolution.First();
                }

                foreach (var quality in matchingQualitiesUnknownResolution)
                {
                    if (quality.Source >= source)
                    {
                        return quality;
                    }
                }
            }

            //Check for Modifier match
            var matchingModifier = Quality.All.Where(q => q.Modifier == modifer);

            var matchingResolution = matchingModifier.Where(q => q.Resolution == resolution)
                                            .OrderBy(q => q.Source)
                                            .ToList();

            var nearestQuality = Quality.Unknown;

            foreach (var quality in matchingResolution)
            {
                if (quality.Source >= source)
                {
                    nearestQuality = quality;
                    break;
                }
            }

            return nearestQuality;
        }
    }
}
