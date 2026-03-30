#if NETFRAMEWORK

using System.Collections.Generic;

static class DeconstructExtensions {

    public static void Deconstruct<TKey, TValue>(this KeyValuePair<TKey, TValue> self, out TKey key, out TValue value) {
        key = self.Key;
        value = self.Value;
    }
}

#endif