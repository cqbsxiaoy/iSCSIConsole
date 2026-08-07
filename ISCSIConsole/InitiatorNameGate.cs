#if !NET20
using System;
using ISCSI.Server;

namespace ISCSIConsole
{
    internal sealed class InitiatorNameGate
    {
        private readonly string m_allowedInitiatorName;

        public InitiatorNameGate(string allowedInitiatorName)
        {
            if (String.IsNullOrEmpty(allowedInitiatorName))
            {
                throw new ArgumentException("Allowed initiator name is empty.", "allowedInitiatorName");
            }

            m_allowedInitiatorName = allowedInitiatorName.Trim();
        }

        public bool Authorize(AuthorizationRequestArgs request)
        {
            if (request == null)
            {
                throw new ArgumentNullException("request");
            }

            return Authorize(request.InitiatorName);
        }

        internal bool Authorize(string initiatorName)
        {
            return String.Equals(m_allowedInitiatorName, initiatorName, StringComparison.OrdinalIgnoreCase);
        }

        public string AllowedInitiatorName
        {
            get
            {
                return m_allowedInitiatorName;
            }
        }
    }
}
#endif
