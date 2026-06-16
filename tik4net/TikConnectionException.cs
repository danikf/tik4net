using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace tik4net
{
    /// <summary>
    /// Any exception from mikrotik session.
    /// </summary>

    [Serializable]
    public abstract class TikConnectionException : Exception
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="TikConnectionException"/> class.
        /// </summary>
        /// <param name="info">The object that holds the serialized object data.</param>
        /// <param name="context">The contextual information about the source or destination.</param>
        protected TikConnectionException(System.Runtime.Serialization.SerializationInfo info, System.Runtime.Serialization.StreamingContext context)
            : base(info, context)
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="TikConnectionException"/> class.
        /// </summary>
        protected TikConnectionException()
            : base()
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="TikConnectionException"/> class.
        /// </summary>
        /// <param name="message">The message.</param>
        protected TikConnectionException(string message)
            : base(message)
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="TikConnectionException"/> class.
        /// </summary>
        /// <param name="message">The message.</param>
        /// <param name="innerException">The inner exception.</param>
        protected TikConnectionException(string message, Exception innerException)
            : base(message, innerException)
        {
        }

        ///// <summary>
        ///// Initializes a new instance of the <see cref="TikConnectionException"/> class.
        ///// </summary>
        ///// <param name="message">The exception message.</param>
        ///// <param name="command">The command sent to target.</param>
        //public TikConnectionException(string message, ITikCommand command)
        //    : this(FormatMessage(message, command, null))
        //{
        //}
    }

    /// <summary>
    /// Exception when command is performed via not opened <see cref="ITikConnection"/>.
    /// </summary>
    public class TikConnectionNotOpenException : TikConnectionException
    {
        /// <summary>
        /// .ctor
        /// </summary>
        /// <param name="message"></param>
        public TikConnectionNotOpenException(string message)
            : base(message)
        {
        }
    }

    /// <summary>
    /// Exception when login failed (invalid credentials)
    /// </summary>
    public class TikConnectionLoginException : TikConnectionException
    {
        /// <summary>
        /// .ctor
        /// </summary>
        public TikConnectionLoginException(Exception innerException)
            : base("Cannot log in. " + innerException.Message, innerException)
        {
        }
    }

    /// <summary>
    /// Thrown when API-SSL is not properly implemented on mikrotik.
    /// See https://github.com/danikf/tik4net/wiki/SSL-connection for details.
    /// </summary>
    public class TikConnectionSSLErrorException : TikConnectionException
    {
        /// <summary>
        /// .ctor
        /// </summary>
        public TikConnectionSSLErrorException(Exception innerException)
            : base("API-SSL error (see https://github.com/danikf/tik4net/wiki/SSL-connection). " + innerException.Message, innerException)
        {
        }
    }

    /// <summary>
    /// Thrown when a feature is invoked on a transport that does not report the required
    /// <see cref="TikConnectionCapability"/>. Check <see cref="ITikConnection"/> support up front with
    /// <see cref="TikConnectionCapabilityExtensions.Supports"/> to avoid it. See the
    /// <see href="https://github.com/danikf/tik4net/wiki/Connection-types-and-capabilities">capability matrix</see>
    /// for which transport supports what.
    /// </summary>
    [Serializable]
    public class TikConnectionCapabilityNotSupportedException : TikConnectionException
    {
        /// <summary>The capability the active transport does not support.</summary>
        public TikConnectionCapability Capability { get; }

        /// <summary>
        /// Initializes a new instance of the <see cref="TikConnectionCapabilityNotSupportedException"/> class.
        /// </summary>
        /// <param name="capability">The capability the transport does not support.</param>
        /// <param name="message">The message.</param>
        public TikConnectionCapabilityNotSupportedException(TikConnectionCapability capability, string message)
            : base(message)
        {
            Capability = capability;
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="TikConnectionCapabilityNotSupportedException"/> class.
        /// </summary>
        /// <param name="info">The object that holds the serialized object data.</param>
        /// <param name="context">The contextual information about the source or destination.</param>
        protected TikConnectionCapabilityNotSupportedException(System.Runtime.Serialization.SerializationInfo info, System.Runtime.Serialization.StreamingContext context)
            : base(info, context)
        {
            Capability = (TikConnectionCapability)info.GetInt32(nameof(Capability));
        }

        /// <inheritdoc/>
        public override void GetObjectData(System.Runtime.Serialization.SerializationInfo info, System.Runtime.Serialization.StreamingContext context)
        {
            base.GetObjectData(info, context);
            info.AddValue(nameof(Capability), (int)Capability);
        }
    }
}
