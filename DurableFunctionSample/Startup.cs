﻿using DurableFunctionSample;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Hosting;

[assembly: WebJobsStartup(typeof(Startup))]
namespace DurableFunctionSample;

public class Startup : IWebJobsStartup
{
    public void Configure(IWebJobsBuilder builder)
    {
    }
}