using InvertedTomato.TikLink.Records;
using System;
using System.Collections.Generic;

namespace InvertedTomato.TikLink.RecordHandlers {
    public class LinkSystemCertificate : SetRecordHandlerBase<SystemCertificate> {
        internal LinkSystemCertificate(Link link) : base(link) { }

        public void Sign(string id) { // TODO: Allow specifing CA
            if (null == id) {
                throw new ArgumentNullException(nameof(id));
            }

            var result = Link.Call("/certificate/sign", new Dictionary<string, string>() {
                {".id", id},
                //{ "name", record.CommonName }
            }).Wait();

            if (result.IsError) {
                result.TryGetTrapAttribute("message", out var message);
                throw new CallException(message);
            }
        }

        public void Sign(SystemCertificate record) {
            if (null == record) {
                throw new ArgumentNullException(nameof(record));
            }

            Sign(record.Id);
        }

        // TODO: impliment other certificate methods: https://wiki.mikrotik.com/wiki/Manual:System/Certificates
    }
}
