// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using Microsoft.Extensions.Configuration;

namespace IncomingCallSample
{
    /// <summary>
    /// Configuration assoociated with the call.
    /// </summary>
    public class CallConfiguration
    {
        private static CallConfiguration callConfiguration = null;
        /// <summary>
        /// The connectionstring of Azure Communication Service resource.
        /// </summary>
        public string ConnectionString { get; private set; }

        /// <summary>
        /// The base url of the applicaiton.
        /// </summary>
        public string AppBaseUrl { get; private set; }

        /// <summary>
        /// The callback url of the application where notification would be received.
        /// </summary>
        public string AppCallbackUrl { get; private set; }

        /// <summary>
        /// The audio file name of the play prompt.
        /// </summary>
        private string AudioFileName;

        /// <summary>
        /// The publicly available url of the audio file which would be played as a prompt.
        /// </summary>
        public string AudioFileUrl => $"{AudioFileName}";

        /// <summary>
        /// The audio file name of the play prompt.
        /// </summary>
        public string TargetParticipant { get; private set; }

        /// <summary>
        /// Accept calls only from particular identifier
        /// </summary>
        public string AcceptCallsFrom { get; private set; }

        public CallConfiguration(string connectionString, string appBaseUrl, string audioFileName, 
            string targetParticipant, string queryString, string acceptCallsFrom)
        {
            ConnectionString = connectionString;
            AppBaseUrl = appBaseUrl;
            AudioFileName = audioFileName;
            TargetParticipant = targetParticipant;
            AppCallbackUrl = $"{AppBaseUrl}/CallAutomationApiCallBack?{queryString}";
            AcceptCallsFrom = acceptCallsFrom;
        }

        public static CallConfiguration GetCallConfiguration(IConfiguration configuration, string queryString)
        {
            if(callConfiguration == null)
            {
                callConfiguration = new CallConfiguration(configuration["ResourceConnectionString"],
                    configuration["AppCallBackUri"],
                    configuration["AudioFileUri"],
                    configuration["TargetParticipant"],
                    queryString,
                    configuration["AcceptCallsFrom"]);
            }

            return callConfiguration;
        }
    }
}
