using System;

namespace tik4net.Winbox
{
    /// <summary>
    /// Raised by the native WinBox M2 operation layer when the router answers a CRUD request with a non-zero
    /// error status. Carries the numeric M2 error code (<see cref="Code"/>, the <c>uff0008</c> value) and the
    /// router's human-readable error string (<see cref="ErrorText"/>, the <c>sff0009</c> value, e.g.
    /// <c>"already have such address"</c>). The connection layer translates this into the matching public
    /// tik4net exception (<see cref="TikAlreadyHaveSuchItemException"/>, <see cref="TikNoSuchItemException"/>,
    /// …) so callers see the same exception types as the API/CLI transports.
    /// </summary>
    internal sealed class WinboxM2OperationException : Exception
    {
        /// <summary>M2 error code from the <c>uff0008</c> field (e.g. <c>0xFE0007</c> = already exists).</summary>
        internal int Code { get; }

        /// <summary>Router-supplied error string from <c>sff0009</c> (may be <c>null</c>).</summary>
        internal string ErrorText { get; }

        internal WinboxM2OperationException(int code, string errorText, string op, int[] handler)
            : base(BuildMessage(code, errorText, op, handler))
        {
            Code = code;
            ErrorText = errorText;
        }

        private static string BuildMessage(int code, string errorText, string op, int[] handler)
        {
            string detail = string.IsNullOrEmpty(errorText) ? "" : $" '{errorText}'";
            return $"WinBox native {op} returned error 0x{code:X}{detail} on handler [{string.Join(",", handler)}].";
        }
    }
}
