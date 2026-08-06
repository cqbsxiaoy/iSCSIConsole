#if !NET20
using System;
using System.Net;
using ISCSI.Server;

namespace ISCSIConsole
{
    internal sealed class SingleClientGate
    {
        private readonly object m_lock = new object();
        private IPAddress m_ownerAddress;

        public bool Authorize(AuthorizationRequestArgs request, out bool ownerAssigned)
        {
            if (request == null)
            {
                throw new ArgumentNullException("request");
            }
            if (request.InitiatorEndPoint == null)
            {
                ownerAssigned = false;
                return false;
            }

            return Authorize(request.InitiatorEndPoint.Address, out ownerAssigned);
        }

        internal bool Authorize(IPAddress sourceAddress, out bool ownerAssigned)
        {
            if (sourceAddress == null)
            {
                throw new ArgumentNullException("sourceAddress");
            }

            lock (m_lock)
            {
                ownerAssigned = false;
                if (m_ownerAddress == null)
                {
                    m_ownerAddress = sourceAddress;
                    ownerAssigned = true;
                    return true;
                }

                return m_ownerAddress.Equals(sourceAddress);
            }
        }

        public IPAddress OwnerAddress
        {
            get
            {
                lock (m_lock)
                {
                    return m_ownerAddress;
                }
            }
        }
    }
}
#endif
