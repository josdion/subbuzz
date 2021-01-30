using System;
using System.Collections.Generic;

namespace SubtitlesParser.Classes
{
    public class SubtitleItem
    {

        //Properties------------------------------------------------------------------
        
        /// <summary>
        /// Start time in milliseconds.
        /// </summary>
        public int StartTime { get; set; }
        /// <summary>
        /// End time in milliseconds.
        /// </summary>
        public int EndTime { get; set; }
        public List<string> Lines { get; set; }
        

        //Constructors-----------------------------------------------------------------

        /// <summary>
        /// The empty constructor
        /// </summary>
        public SubtitleItem()
        {
            this.Lines = new List<string>();
        }


        // Methods --------------------------------------------------------------------------

        public override string ToString()
        {
            var startTs = new TimeSpan(0, 0, 0, 0, StartTime);
            var endTs = new TimeSpan(0, 0, 0, 0, EndTime);

            var res = string.Format("{0} --> {1}: {2}", startTs.ToString("G"), endTs.ToString("G"), string.Join(Environment.NewLine, Lines));
            return res;
        }

    }
}