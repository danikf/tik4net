using InvertedTomato.TikLink.Records;
using System;
using System.Collections.Generic;

namespace InvertedTomato.TikLink.Vanity {
    public class LinkSystemCertificate {
        private readonly Link Link;

        internal LinkSystemCertificate(Link link) {
            Link = link;
        }

        public IList<SystemCertificate> List(string[] properties = null, Dictionary<string, string> filter = null) {
            return Link.List<SystemCertificate>(properties, filter);
        }

        public SystemCertificate Get(string id, string[] properties = null) {
            return Link.Get<SystemCertificate>(id, properties);
        }

        public void Add(SystemCertificate record, string[] properties = null) {
            Link.Add(record, properties);
        }

        public void Update(SystemCertificate record, string[] properties = null) {
            Link.Update(record, properties);
        }

        public void Delete(string id) {
            Link.Delete<SystemCertificate>(id);
        }

        public void Delete(SystemCertificate record) {
            Link.Delete(record);
        }

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
