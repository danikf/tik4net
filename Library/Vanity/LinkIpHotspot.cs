namespace InvertedTomato.TikLink.Vanity {
    public class LinkIpHotspot {
        public readonly LinkIpHotspotActive Active;
        public readonly LinkIpHotspotUser User;
        public readonly LinkIpHotspotUserProfile UserProfile;

        private readonly Link Link;

        internal LinkIpHotspot(Link link) {
            Link = link;

            Active = new LinkIpHotspotActive(Link);
            User = new LinkIpHotspotUser(Link);
            UserProfile = new LinkIpHotspotUserProfile(Link);
        }
    }
}
