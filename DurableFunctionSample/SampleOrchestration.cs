using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using DurableTask.Core.History;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.DurableTask;
using Microsoft.Azure.WebJobs.Extensions.DurableTask.TypedInterfaces;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.Azure.WebJobs.Extensions.OpenApi.Core.Attributes;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Serialization;

namespace DurableFunctionSample
{

    public class CreateWorkflowRequest
    {
        public string RequestId { get; set; }
    }
    public class WorkflowData
    {
        public Guid Id { get; set; }
        public TaskOneData TaskOneData { get; set; }
        public TaskTwoData TaskTwoData { get; set; }
        public TaskThreeData TaskThreeData { get; set; }

        public JArray WorkflowHistory { get; set; }
    }

    public class TaskOneData
    {
        public Guid id { get; set; }
        public Guid result { get; set; }
        
        public JArray TaskHistory { get; set; }
    }

    public class TaskTwoData
    {
        public Guid id { get; set; }
        public Guid result { get; set; }

        public JArray TaskHistory { get; set; }
    }

    public class TaskThreeData
    {
        public Guid id { get; set; }
        public Guid result { get; set; }

        public JArray TaskHistory { get; set; }
    }

    public class SampleOrchestration
    {
        private static HttpClient httpClient = new HttpClient();
        private IDurableOrchestrationContext _context;


        private static JsonSerializerSettings jsonSerializerSettings = new JsonSerializerSettings
        {
            ReferenceLoopHandling = ReferenceLoopHandling.Ignore,
            PreserveReferencesHandling = PreserveReferencesHandling.None,
            ContractResolver = new CamelCasePropertyNamesContractResolver(),
            Formatting = Formatting.Indented,
            NullValueHandling = NullValueHandling.Ignore
        };

        [FunctionName("TaskWorkflow")]
        public async Task<WorkflowData> TaskWorkflow([OrchestrationTrigger] ITypedDurableOrchestrationContext context)
        {
            var data = context.GetInput<WorkflowData>();
            data.TaskOneData = await context.Activities.TaskOne(new TaskOneData { id = data.Id}); 
            data.TaskTwoData = await context.Activities.TaskTwo(new TaskTwoData { id = data.Id}); 
            data.TaskThreeData = await context.Activities.TaskThree(new TaskThreeData { id = data.Id }); 
            return data;
        }


        [FunctionName("TaskOne")]
        public async  Task<TaskOneData> TaskOne([ActivityTrigger] IDurableActivityContext context, ILogger log)
        {
            var data = context.GetInput<TaskOneData>();
            Thread.Sleep(TimeSpan.FromSeconds(10));
            var response = await httpClient.GetAsync("https://www.uuidtools.com/api/generate/v1");
            var uid = JsonConvert.DeserializeObject<string[]>(await response.Content.ReadAsStringAsync());
            data.result = Guid.Parse(uid.First());
            return data;
        }


        [FunctionName("TaskTwo")]
        public async Task<TaskTwoData> TaskTwo([ActivityTrigger] IDurableActivityContext context, ILogger log)
        {
            var data = context.GetInput<TaskTwoData>();
            Thread.Sleep(TimeSpan.FromSeconds(10));
            var response = await httpClient.GetAsync("https://www.uuidtools.com/api/generate/v1");
            var uid = JsonConvert.DeserializeObject<string[]>(await response.Content.ReadAsStringAsync());
            data.result = Guid.Parse(uid.First());
            return data;
        }

        [FunctionName("TaskThree")]
        public async Task<TaskThreeData> TaskThree([ActivityTrigger] IDurableActivityContext context, ILogger log)
        {
            //throw new TimeoutException("Http Request: Time out simulation");
            var data = context.GetInput<TaskThreeData>();
            Thread.Sleep(TimeSpan.FromSeconds(30));
            var response = await httpClient.GetAsync("https://www.uuidtools.com/api/generate/v1");
            var uid = JsonConvert.DeserializeObject<string[]>(await response.Content.ReadAsStringAsync());
            data.result = Guid.Parse(uid.First());
            return data;
        }
        

        [OpenApiOperation(operationId: "CreateWorkflow")]
        [OpenApiRequestBody("application/json", typeof(CreateWorkflowRequest))]
        [OpenApiResponseWithBody(statusCode: HttpStatusCode.OK, contentType: "application/json",
            bodyType: typeof(JToken))]
        [FunctionName("CreateWorkflow")]
        public static async Task<IActionResult> CreateWorkflow(
            [HttpTrigger(AuthorizationLevel.Anonymous, "post")] HttpRequestMessage request,
            [DurableClient] ITypedDurableClient starter,
            ILogger log)
        {
            // Function input comes from the request content.
            var requestBody = await request.Content.ReadAsStringAsync();

            var createWorkflowRequest = JsonConvert.DeserializeObject<CreateWorkflowRequest>(requestBody);
            var status = await starter.GetStatusAsync(createWorkflowRequest.RequestId, true, true, true);


            if (status != null && (status.RuntimeStatus == OrchestrationRuntimeStatus.Failed))
            {
                await starter.RewindAsync(createWorkflowRequest.RequestId, "");
            }

            if (status != null && (status.RuntimeStatus == OrchestrationRuntimeStatus.Suspended))
            {
                await starter.ResumeAsync(createWorkflowRequest.RequestId, "");
            }

            if (status == null || (status.RuntimeStatus != OrchestrationRuntimeStatus.Suspended &&
                                   status.RuntimeStatus != OrchestrationRuntimeStatus.Failed &&
                                   status.RuntimeStatus != OrchestrationRuntimeStatus.Pending &&
                                   status.RuntimeStatus != OrchestrationRuntimeStatus.Running))
            {
                await starter.Orchestrations.StartTaskWorkflow(new WorkflowData
                {
                    Id = Guid.Parse(createWorkflowRequest.RequestId)
                }, createWorkflowRequest.RequestId);
            }

            log.LogInformation($"Started orchestration with ID = '{createWorkflowRequest.RequestId}'.");
            await starter.WaitForCompletionOrCreateCheckStatusResponseAsync(request,
                createWorkflowRequest.RequestId,
                TimeSpan.FromSeconds(25));
            status = await starter.GetStatusAsync(createWorkflowRequest.RequestId);
            WorkflowData response = null;
            switch (status.RuntimeStatus)
            {
                case OrchestrationRuntimeStatus.Completed:
                    response = status.Output.ToObject<WorkflowData>();
                    break;
                case OrchestrationRuntimeStatus.Running:
                case OrchestrationRuntimeStatus.Pending:
                case OrchestrationRuntimeStatus.ContinuedAsNew:
                case OrchestrationRuntimeStatus.Unknown:
                default:
                    await starter.SuspendAsync(createWorkflowRequest.RequestId, "Timeout!");
                    break;
            }
            var httpResponse = response != null ? new OkObjectResult(response) : new OkObjectResult(status);
            return httpResponse;
        }
    }
}