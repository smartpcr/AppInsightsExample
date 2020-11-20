using Microsoft.AspNetCore.Http;

namespace Common.Instrumentation
{
    using System;
    using System.Diagnostics;
    using System.Linq;
    using System.Runtime.CompilerServices;
    using System.Threading;
    using Microsoft.ApplicationInsights;
    using Microsoft.ApplicationInsights.DataContracts;
    using Microsoft.ApplicationInsights.Extensibility;

    /// <summary>
    /// 
    /// </summary>
    public static class CorrelationManager
    {
        private static readonly AsyncLocal<Activity> _callActivity = new AsyncLocal<Activity>();
        private const string RequestTelemetryKey = "request-id";

        public static OperationScope StartOperation(this object caller, 
            TelemetryClient telemetry,
            IHttpContextAccessor contextAccessor,
            [CallerMemberName]string operationName = "")
        {
            operationName = caller == null ? operationName : caller.GetType().Name + "." + operationName;
            var activity = new Activity(operationName);
            if (contextAccessor != null)
            {
                var context = contextAccessor.HttpContext;
                if (context.Request.Headers.ContainsKey(RequestTelemetryKey))
                {
                    if (context.Request.Headers[RequestTelemetryKey].FirstOrDefault() is string operationId)
                    {
                        activity.SetParentId(operationId);
                        Console.WriteLine($"OperationId received from request: {operationId}");
                    }
                }
            }

            if (activity.ParentId == null && _callActivity.Value != null)
            {
                var parentActivity = _callActivity.Value;
                activity.SetParentId(parentActivity.Id);
            }

            _callActivity.Value = activity;
            activity.Start();

            return new OperationScope(telemetry, activity, caller?.GetType().Assembly.GetName().Name);
        }

        public static OperationScope StartOperation(this object caller, 
            TelemetryClient telemetry,
            string parentOperationId = null,
            [CallerMemberName]string operationName = "")
        {
            operationName = caller == null ? operationName : caller.GetType().Name + "." + operationName;
            var activity = new Activity(operationName);
            if (parentOperationId != null)
            {
                activity.SetParentId(parentOperationId);
            }
            _callActivity.Value = activity;

            return new OperationScope(telemetry, activity, caller?.GetType().Assembly.GetName().Name);
        }
    }

    public class OperationScope : IDisposable
    {
        private readonly TelemetryClient _telemetry;
        private IOperationHolder<RequestTelemetry> _requestOperation;
        private IOperationHolder<DependencyTelemetry> _dependencyOperation;
        public string OperationName => Activity.OperationName;
        public Activity Activity { get; private set; }

        public OperationScope(TelemetryClient telemetry, Activity activity, string callerAssembly)
        {
            _telemetry = telemetry ?? throw new ArgumentNullException(nameof(telemetry));
            Activity = activity ?? throw new ArgumentNullException(nameof(activity));
            if (activity.ParentId == null)
            {
                _requestOperation = _telemetry.StartOperation<RequestTelemetry>(activity);
                _requestOperation.Telemetry.Context.Cloud.RoleName = _requestOperation.Telemetry.Context.Cloud.RoleName??callerAssembly;
            }
            else
            {
                _dependencyOperation = _telemetry.StartOperation<DependencyTelemetry>(activity);
                _dependencyOperation.Telemetry.Type = callerAssembly ?? "method";
                _dependencyOperation.Telemetry.Context.Cloud.RoleName = _dependencyOperation.Telemetry.Context.Cloud.RoleName ?? callerAssembly;
            }
        }

        /// <summary>
        /// this calls dispose on <see cref="IOperationHolder{T}"/>, which stops operation,
        /// track call duration and then stop associated activity
        /// </summary>
        public void Dispose()
        {
            if (_requestOperation != null)
            {
                _telemetry.StopOperation(_requestOperation);
                _requestOperation = null;
            }
            if (_dependencyOperation != null)
            {
                _telemetry.StopOperation(_dependencyOperation);
                _dependencyOperation = null;
            }
        }
    }
}