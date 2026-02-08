using System;
using System.Collections.Generic;
using System.Net;
using System.Text;
using System.Text.Json.Serialization;
using System.Text.Json;

namespace subbuzz.Providers.SubSourceApi.Models
{
    public class ApiResponse<T>
    {
        public HttpStatusCode Code { get; }

        public string Body { get; } = string.Empty;

        public T? Data { get; }

        public bool Ok => (int)Code >= 200 && (int)Code < 300;

        public ApiResponse(T data, HttpResponse response)
        {
            Data = data;
            Code = response.Code;
            Body = response.Body;

            if (!Ok && string.IsNullOrWhiteSpace(Body) && !string.IsNullOrWhiteSpace(response.Reason))
            {
                Body = response.Reason;
            }
        }

        public ApiResponse(HttpResponse response, params string[] context)
        {
            Code = response.Code;
            Body = response.Body;

            if (!Ok && string.IsNullOrWhiteSpace(Body) && !string.IsNullOrWhiteSpace(response.Reason))
            {
                Body = response.Reason;
            }

            if (typeof(T) == typeof(string))
            {
                Data = (T)(object)Body;
                return;
            }

            if (!Ok)
            {
                // don't bother parsing json if HTTP status code is bad
                return;
            }

            try
            {
                Data = JsonSerializer.Deserialize<T>(Body) ?? default;
            }
            catch (Exception e)
            {
                throw new JsonException($"Failed to parse response, code: {Code}, context: {string.Join(", ", context)}, body: \n{(string.IsNullOrWhiteSpace(Body) ? "\"\"" : Body)}", e);
            }
        }

        [JsonConstructor]
        public ApiResponse(HttpStatusCode code, string body, T data) =>
            (Code, Body, Data) = (code, body, data);
    }
}
