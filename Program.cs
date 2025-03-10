using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Rewrite;

var builder = WebApplication.CreateBuilder(args);

// register new service
builder.Services.AddSingleton<ITaskService>(new InMemoryTaskService());

var app = builder.Build();

app.UseRewriter(new RewriteOptions().AddRedirect("tasks/(.*)/(.*)", "todos/$1/$2"));
app.UseRewriter(new RewriteOptions().AddRedirect("tasks", "todos"));

app.Use( // middleware
    async (context, next) =>
    {
        Console.WriteLine(
            $"[{context.Request.Method} {context.Request.Path} {DateTime.UtcNow}] Started."
        );
        await next(context);
        Console.WriteLine(
            $"[{context.Request.Method} {context.Request.Path} {DateTime.UtcNow}] Finished."
        );
    }
);

var todos = new List<Todo>();

// GET with a Dependency Injection
app.MapGet(
    "/todos",
    Results<Ok<List<Todo>>, NotFound> (ITaskService service) =>
    {
        return TypedResults.Ok(service.GetTodos());
    }
);

app.MapGet(
    "/todos/{id}",
    Results<Ok<Todo>, NotFound> (int id, ITaskService service) =>
    {
        var targetTodo = service.GetTodoById(id);
        return targetTodo is null ? TypedResults.NotFound() : TypedResults.Ok(targetTodo);
    }
);

app.MapPost(
        "/todos",
        (Todo task, ITaskService service) =>
        {
            task.Id = todos.Count + 1;
            service.AddTodo(task);
            return TypedResults.Created("/todos/{id}", task);
        }
    )
    .AddEndpointFilter( // Endpoint filter
        async (context, next) =>
        {
            var taskArgument = context.GetArgument<Todo>(0);
            var errors = new Dictionary<string, string[]>();
            if (taskArgument.DueDate < DateTime.UtcNow)
            {
                errors.Add(nameof(Todo.DueDate), ["Cannot have due date in the past."]);
            }
            if (taskArgument.IsCompleted)
            {
                errors.Add(nameof(Todo.IsCompleted), ["Cannot add completed todo."]);
            }

            if (errors.Count > 0)
            {
                return Results.ValidationProblem(errors);
            }

            return await next(context);
        }
    );

app.MapPatch(
    "/todos/{id}",
    Results<Ok<Todo>, NotFound> (int id, ITaskService service) =>
    {
        var targetTodo = service.GetTodoById(id);
        if (targetTodo is null)
            return TypedResults.NotFound();

        targetTodo.IsCompleted = !targetTodo.IsCompleted;
        return TypedResults.Ok(targetTodo);
    }
);

app.MapDelete(
    "/todos/{id}",
    Results<NoContent, NotFound> (int id, ITaskService service) =>
    {
        service.DeleteTodoById(id);
        return TypedResults.NoContent();
    }
);

app.Run();

public record Todo(int Id, string Name, DateTime DueDate, bool IsCompleted)
{
    public int Id { get; set; } = Id;
    public bool IsCompleted { get; set; } = IsCompleted;
}

// Interface for dependency
interface ITaskService
{
    Todo? GetTodoById(int id);

    List<Todo> GetTodos();

    void DeleteTodoById(int id);

    Todo AddTodo(Todo task);
}

// Concrete implementation of Interface
class InMemoryTaskService : ITaskService
{
    private readonly List<Todo> _todos = [];

    public Todo AddTodo(Todo task)
    {
        _todos.Add(task);
        return task;
    }

    public List<Todo> GetTodos()
    {
        return _todos;
    }

    public Todo? GetTodoById(int id)
    {
        return _todos.SingleOrDefault(task => task.Id == id);
    }

    public void DeleteTodoById(int id)
    {
        _todos.RemoveAll(task => task.Id == id);
    }
}
