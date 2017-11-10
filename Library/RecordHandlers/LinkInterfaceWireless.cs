using InvertedTomato.TikLink.Records;
using System.Collections.Generic;

namespace InvertedTomato.TikLink.RecordHandlers {
    public class LinkInterfaceWireless : SetRecordHandlerBase<InterfaceWireless> {
        public readonly LinkInterfaceWirelessAccessList AccessList;
        public readonly LinkInterfaceWirelessRegistrationTable RegistrationTable;
        public readonly LinkInterfaceWirelessSecurityProfile SecurityProfile;

        public LinkInterfaceWireless(Link link) : base(link) {
            AccessList = new LinkInterfaceWirelessAccessList(Link);
            RegistrationTable = new LinkInterfaceWirelessRegistrationTable(Link);
            SecurityProfile = new LinkInterfaceWirelessSecurityProfile(Link);
        }
    }
}
