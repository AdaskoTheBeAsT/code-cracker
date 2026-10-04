using System;
using System.Collections.Generic;

namespace CodeCracker.CSharp.Usage.MethodAnalyzers
{
    /// <summary>
    /// Describes a target method signature, its argument validator, and the argument used for diagnostic locations.
    /// </summary>
    public class MethodInformation
    {
        /// <summary>
        /// Gets or sets the simple method or constructor type name used for syntax matching.
        /// </summary>
        public string MethodName { get; set; }
        /// <summary>
        /// Gets or sets the method symbol's display string used for exact signature matching.
        /// </summary>
        public string MethodFullDefinition { get; set; }
        /// <summary>
        /// Gets or sets the zero-based argument index used as the location of validation diagnostics.
        /// </summary>
        public int ArgumentIndex { get; set; }
        /// <summary>
        /// Gets or sets the callback that validates constant argument values and throws on invalid input.
        /// </summary>
        public Action<List<object>> MethodAction { get; set; }

        /// <summary>
        /// Initializes the target method signature, argument validator, and diagnostic argument index.
        /// </summary>
        /// <param name="methodName">The simple method or constructor type name used for syntax matching.</param>
        /// <param name="methodFullDefinition">The method symbol's display string used for exact signature matching.</param>
        /// <param name="methodAction">The callback that validates constant argument values and throws on invalid input.</param>
        /// <param name="argumentIndex">The zero-based argument index used as the location of validation diagnostics.</param>
        public MethodInformation(string methodName, string methodFullDefinition, Action<List<object>> methodAction, int argumentIndex = 0)
        {
            MethodName = methodName;
            MethodFullDefinition = methodFullDefinition;
            ArgumentIndex = argumentIndex;
            MethodAction = methodAction;
        }
    }
}