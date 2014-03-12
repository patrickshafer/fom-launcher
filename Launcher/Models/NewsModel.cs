﻿using System;
using System.Collections.Generic;
using System.ServiceModel.Syndication;
using System.Xml;



namespace FoM.Launcher.Models
{
    public class NewsModel
    {
        private const string RSSURL = @"http://forums.faceofmankind.com/external.php?forumids=299&type=rss2";
        private List<NewsItem> _NewsItems;
        public void OpenRSS()
        {
            XmlTextReader Foobar = new XmlTextReader(RSSURL);
            SyndicationFeed feed = SyndicationFeed.Load(Foobar);
        }
        public List<NewsItem> NewsItems
        {
            get
            {
                if (this._NewsItems == null)
                {
                    this._NewsItems = new List<NewsItem>();
                    this._NewsItems.Add(new NewsItem() { Title = "Title one", Summary = "Summary one", PublishDate = DateTime.Now.AddDays(-5) });
                    this._NewsItems.Add(new NewsItem() { Title = "Title 2", Summary = "Lorem ipsum dolor sit amet, consectetur adipiscing elit. Etiam at condimentum massa. Sed ac auctor nunc. Vivamus dignissim est a eleifend semper. Integer facilisis mi a lorem auctor posuere. Phasellus malesuada in dolor sed ornare. Fusce at odio ac dui porttitor posuere. Ut eu tempor lorem.", PublishDate = DateTime.Now.AddDays(-4) });
                    this._NewsItems.Add(new NewsItem() { Title = "Title three", Summary = "Summary three", PublishDate = DateTime.Now.AddDays(-1) });
                }
                return this._NewsItems;
            }
        }
    }
    public class NewsItem
    {
        public string Title { get; set; }
        public string Summary { get; set; }
        public DateTime PublishDate { get; set; }
    }
}
