#if NETFRAMEWORK

using System.Collections.Generic;

static class DeconstructExtensions {

    public static void Deconstruct<TKey, TValue>(this KeyValuePair<TKey, TValue> self, out TKey key, out TValue value) {
        key = self.Key;
        value = self.Value;
    }
}

namespace System.Diagnostics.CodeAnalysis {

    /// <summary>Specifies that when a method returns <see cref="P:System.Diagnostics.CodeAnalysis.NotNullWhenAttribute.ReturnValue" />, the parameter will not be <see langword="null" /> even if the corresponding type allows it.</summary>
    [AttributeUsage(AttributeTargets.Parameter, Inherited = false)]
    public sealed class NotNullWhenAttribute : Attribute
    {
        /// <summary>Gets the return value condition.</summary>
        /// <returns>The return value condition. If the method returns this value, the associated parameter will not be <see langword="null" />.</returns>
        public bool ReturnValue { get; }

        /// <summary>Initializes the attribute with the specified return value condition.</summary>
        /// <param name="returnValue">The return value condition. If the method returns this value, the associated parameter will not be <see langword="null" />.</param>
        public NotNullWhenAttribute(bool returnValue)
        {
            ReturnValue = returnValue;
        }
    }

}

#endif