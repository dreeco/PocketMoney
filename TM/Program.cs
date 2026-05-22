

using Application;
using Domain.Entities;
using Infrastructure.DataAccess;
using Microsoft.Extensions.Hosting;

Console.WriteLine("Hello, World!");

var builder = Host.CreateApplicationBuilder(args);

var configuration = builder.Configuration;

var repo = new CleaningTasksRepository(configuration);
var taskSelector = new TaskSelector(repo);
var nextToDo = await taskSelector.GetTaskFor(new Member("Lucie"));

var balance = await taskSelector.GetMemberBalance(new Member("Lucie"));

Console.WriteLine(nextToDo.Value.name);
Console.WriteLine($"Balance: {balance.Value.amount} cents, Pending: {balance.Value.pendingAmount} cents");