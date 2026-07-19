using iText.Kernel.Geom;
using iText.Kernel.Pdf.Canvas.Parser;
using iText.Kernel.Pdf.Canvas.Parser.Data;
using iText.Kernel.Pdf.Canvas.Parser.Listener;
using System;
using System.Collections.Generic;
using System.Linq;

namespace VMSign.Web.Services
{
    public class LocationTextExtractionStrategyEx : LocationTextExtractionStrategy
    {
        private List<LocationTextExtractionStrategyEx.ExtendedTextChunk> m_DocChunks = new List<ExtendedTextChunk>();
        private List<LocationTextExtractionStrategyEx.LineInfo> m_LinesTextInfo = new List<LineInfo>();
        public List<SearchResult> m_SearchResultsList = new List<SearchResult>();
        private String m_SearchText;
        public const float PDF_PX_TO_MM = 0.3528f;
        public float m_PageSizeY;
        public bool m_IsFindNearest;


        public LocationTextExtractionStrategyEx(String sSearchText, float fPageSizeY, bool bIsFindNearest)
            : base()
        {
            this.m_SearchText = sSearchText;
            this.m_PageSizeY = fPageSizeY;
            this.m_IsFindNearest = bIsFindNearest;
        }

        public LocationTextExtractionStrategyEx(String sSearchText, float fPageSizeY)
            : base()
        {
            this.m_SearchText = sSearchText;
            this.m_PageSizeY = fPageSizeY;
        }

        private void searchText()
        {
            var idx = m_LinesTextInfo.Count - 1;
            for (int i = idx; i > 0; i--)
            {
                LineInfo aLineInfo = m_LinesTextInfo[i];
                int iIndex = aLineInfo.m_Text.ToLower().IndexOf(m_SearchText.ToLower());
                if (iIndex != -1)
                {
                    TextRenderInfo aFirstLetter = aLineInfo.m_LineCharsList.ElementAt(0);
                    SearchResult aSearchFirstResult = new SearchResult(aFirstLetter, m_PageSizeY);

                    TextRenderInfo aLastLetter = aLineInfo.m_LineCharsList.ElementAt(aLineInfo.m_Text.Length - 1);
                    SearchResult aSearchLastResult = new SearchResult(aLastLetter, m_PageSizeY);
                    var fHeight = aSearchFirstResult.fHeight > aSearchLastResult.fHeight ? aSearchFirstResult.fHeight : aSearchLastResult.fHeight;
                    SearchResult aSearchResult = new SearchResult(aSearchFirstResult.iPosLX, aSearchFirstResult.iPosLY, aSearchLastResult.iPosRX, aSearchLastResult.iPosRY, fHeight);
                    this.m_SearchResultsList.Add(aSearchResult);
                    break;
                }
            }
        }

        private void findNearestText()
        {
            // Find the line containing the search text, working backwards
            int idx = m_LinesTextInfo.Count - 1;
            for (int i = idx; i >= 0; i--)
            {
                LineInfo aLineInfo = m_LinesTextInfo[i];
                int iIndex = aLineInfo.m_Text
                    .ToLower()
                    .IndexOf(m_SearchText.ToLower());
                if (iIndex != -1)
                {
                    idx = i;
                    break;
                }
            }

            // Now find the nearest previous line with non-empty text
            for (int i = idx - 1; i >= 0; i--)
            {
                LineInfo aLineInfo = m_LinesTextInfo[i];
                string textTrim = aLineInfo.m_Text.Trim();
                // Only consider non-empty lines with at least one character render info
                if (!string.IsNullOrEmpty(textTrim) && aLineInfo.m_LineCharsList.Count > 0)
                {
                    // Use the first character info
                    TextRenderInfo aFirstLetter = aLineInfo.m_LineCharsList[0];
                    SearchResult aSearchFirstResult = new SearchResult(aFirstLetter, m_PageSizeY);

                    // Use the last character info (guard for empty or mismatched length)
                    int lastIdx = aLineInfo.m_LineCharsList.Count - 1;
                    TextRenderInfo aLastLetter = aLineInfo.m_LineCharsList[lastIdx];
                    SearchResult aSearchLastResult = new SearchResult(aLastLetter, m_PageSizeY);

                    float fHeight = Math.Max(aSearchFirstResult.fHeight, aSearchLastResult.fHeight);
                    SearchResult aSearchResult = new SearchResult(
                        aSearchFirstResult.iPosLX,
                        aSearchFirstResult.iPosLY,
                        aSearchLastResult.iPosRX,
                        aSearchLastResult.iPosRY,
                        fHeight
                    );
                    this.m_SearchResultsList.Add(aSearchResult);
                    break;
                }
            }
        }

        private void groupChunksbyLine()
        {
            LocationTextExtractionStrategyEx.ExtendedTextChunk textChunk1 = null;
            LocationTextExtractionStrategyEx.LineInfo textInfo = null;
            foreach (LocationTextExtractionStrategyEx.ExtendedTextChunk textChunk2 in this.m_DocChunks)
            {
                if (textChunk1 == null)
                {
                    textInfo = new LocationTextExtractionStrategyEx.LineInfo(textChunk2);
                    this.m_LinesTextInfo.Add(textInfo);
                }
                else if (textChunk2.sameLine(textChunk1))
                {
                    textInfo.appendText(textChunk2);
                }
                else
                {
                    textInfo = new LocationTextExtractionStrategyEx.LineInfo(textChunk2);
                    this.m_LinesTextInfo.Add(textInfo);
                }
                textChunk1 = textChunk2;
            }
        }

        public override string GetResultantText()
        {
            groupChunksbyLine();
            if (m_IsFindNearest)
            {
                findNearestText();
            }
            else
            {
                searchText();
            }
            //In this case the return value is not useful
            return "";
        }

        // iText 7's LocationTextExtractionStrategy replaced the overridable RenderText(TextRenderInfo)
        // with the EventOccurred(IEventData, EventType) listener entry point. We intercept RENDER_TEXT
        // to build our own chunk list, then delegate to base (which maintains a SEPARATE internal list,
        // so there is no double-processing of our data). Verified by the text-position placement path.
        public override void EventOccurred(IEventData data, EventType type)
        {
            if (type == EventType.RENDER_TEXT && data is TextRenderInfo renderInfo)
            {
                RenderText(renderInfo);
            }
            base.EventOccurred(data, type);
        }

        public void RenderText(TextRenderInfo renderInfo)
        {
            LineSegment baseline = renderInfo.GetBaseline();
            //Create ExtendedChunk
            ExtendedTextChunk aExtendedChunk = new ExtendedTextChunk(renderInfo.GetText(), baseline.GetStartPoint(), baseline.GetEndPoint(), renderInfo.GetSingleSpaceWidth(), renderInfo.GetCharacterRenderInfos().ToList());
            this.m_DocChunks.Add(aExtendedChunk);
        }

        public class ExtendedTextChunk
        {
            public string m_text;
            private Vector m_startLocation;
            private Vector m_endLocation;
            private Vector m_orientationVector;
            private int m_orientationMagnitude;
            private int m_distPerpendicular;
            private float m_charSpaceWidth;
            public List<TextRenderInfo> m_ChunkChars;


            public ExtendedTextChunk(string txt, Vector startLoc, Vector endLoc, float charSpaceWidth, List<TextRenderInfo> chunkChars)
            {
                this.m_text = txt;
                this.m_startLocation = startLoc;
                this.m_endLocation = endLoc;
                this.m_charSpaceWidth = charSpaceWidth;
                this.m_orientationVector = this.m_endLocation.Subtract(this.m_startLocation).Normalize();
                // iText 7's Vector has no C# indexer; use Get(dimension) instead of [index].
                this.m_orientationMagnitude = (int)(Math.Atan2((double)this.m_orientationVector.Get(Vector.I2), (double)this.m_orientationVector.Get(Vector.I1)) * 1000.0);
                this.m_distPerpendicular = (int)this.m_startLocation.Subtract(new Vector(0.0f, 0.0f, 1f)).Cross(this.m_orientationVector).Get(Vector.I3);
                this.m_ChunkChars = chunkChars;
            }


            public bool sameLine(LocationTextExtractionStrategyEx.ExtendedTextChunk textChunkToCompare)
            {
                return this.m_orientationMagnitude == textChunkToCompare.m_orientationMagnitude && this.m_distPerpendicular == textChunkToCompare.m_distPerpendicular;
            }


        }

        public class SearchResult
        {
            public float iPosLX;
            public float iPosLY;
            public float iPosRX;
            public float iPosRY;
            public float fHeight;

            public SearchResult(float iPosLX, float iPosLY, float iPosRX, float iPosRY)
            {
                this.iPosLX = iPosLX;
                this.iPosLY = iPosLY;
                this.iPosRX = iPosRX;
                this.iPosRY = iPosRY;
            }

            public SearchResult(float iPosLX, float iPosLY, float iPosRX, float iPosRY, float fHeight)
            {
                this.iPosLX = iPosLX;
                this.iPosLY = iPosLY;
                this.iPosRX = iPosRX;
                this.iPosRY = iPosRY;
                this.fHeight = fHeight;
            }

            public SearchResult(TextRenderInfo aCharcter, float fPageSizeY)
            {
                // Top
                Vector d = aCharcter.GetDescentLine().GetStartPoint();
                float dx = d.Get(Vector.I1);
                float dy = d.Get(Vector.I2);
                // Mid
                Vector b = aCharcter.GetBaseline().GetStartPoint();
                float bx = b.Get(Vector.I1);
                float by = b.Get(Vector.I2);
                // Height
                Vector a = aCharcter.GetAscentLine().GetStartPoint();
                float ax = a.Get(Vector.I1);
                float ay = a.Get(Vector.I2);
                fHeight = ay - dy;

                //Get position of upperLeft coordinate
                Vector vLeft = aCharcter.GetBaseline().GetStartPoint();
                //PosX
                float fPosLX = vLeft.Get(Vector.I1);
                //PosY
                float fPosLY = vLeft.Get(Vector.I2);
                //Transform to mm and get y from top of page
                //iPosX = Convert.ToInt32(fPosX * PDF_PX_TO_MM);
                //iPosY = Convert.ToInt32((fPageSizeY - fPosY) * PDF_PX_TO_MM);
                iPosLX = fPosLX;
                iPosLY = fPosLY;

                //Get position of upperLeft coordinate
                Vector vRight = aCharcter.GetBaseline().GetEndPoint();
                //PosX
                float fPosRX = vRight.Get(Vector.I1);
                //PosY
                float fPosRY = vRight.Get(Vector.I2);
                //Transform to mm and get y from top of page
                //iPosX = Convert.ToInt32(fPosX * PDF_PX_TO_MM);
                //iPosY = Convert.ToInt32((fPageSizeY - fPosY) * PDF_PX_TO_MM);
                iPosRX = fPosRX;
                iPosRY = fPosRY;
            }
        }

        public class LineInfo
        {
            public string m_Text;
            public List<TextRenderInfo> m_LineCharsList;

            public LineInfo(LocationTextExtractionStrategyEx.ExtendedTextChunk initialTextChunk)
            {
                this.m_Text = initialTextChunk.m_text;
                this.m_LineCharsList = initialTextChunk.m_ChunkChars;
            }

            public void appendText(LocationTextExtractionStrategyEx.ExtendedTextChunk additionalTextChunk)
            {
                m_LineCharsList.AddRange(additionalTextChunk.m_ChunkChars);
                this.m_Text += additionalTextChunk.m_text;
            }
        }
    }
}
