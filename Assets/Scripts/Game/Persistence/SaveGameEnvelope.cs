using System;

namespace ROC.Persistence
{
    [Serializable]
    public class SaveGameEnvelope<T>
    {
        public int Version = 1;
        public T Data;
    }
}