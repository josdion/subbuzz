namespace subbuzz.Parser.Qualities
{
    public class QualityDefinition
    {
        public Quality Quality { get; set; }

        public string Title { get; set; }
        public string GroupName { get; set; }
        public int Weight { get; set; }

        public double? MinSize { get; set; }
        public double? MaxSize { get; set; }
        public double? PreferredSize { get; set; }

        public QualityDefinition()
        {
        }

        public QualityDefinition(Quality quality)
        {
            Quality = quality;
            Title = quality.Name;
        }

        public override string ToString()
        {
            return Quality.Name;
        }
    }
}
