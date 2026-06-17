# Refonte TaxiDjibouti — Plan d'implémentation : Fondations + module Tarification

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Poser le squelette du monolithe modulaire .NET 10 (SharedKernel + couches + hôte Aspire) et le valider de bout en bout avec une première verticale complète (module Tarification), CQRS sans MediatR.

**Architecture:** Monolithe modulaire pragmatique (modules = dossiers dans 5 projets en couches). CQRS via `ICommandHandler`/`IQueryHandler` maison + Scrutor (scan + décorateurs validation/logging). Result pattern. Endpoints `IEndpoint` (Minimal API). EF Core 10 + PostgreSQL via Aspire.

**Tech Stack:** .NET 10, Aspire 13.1.0, EF Core 10 + Npgsql, Scrutor 7, FluentValidation 12, Ardalis.Specification 9, Scalar, Serilog, NetArchTest, xUnit.

**Spec de référence :** `C:\prjRecherche\TaxiDjibouti\docs\superpowers\specs\2026-06-16-migration-net10-clean-archi-design.md`
**Code de référence (comportement) :** `C:\prjRecherche\TaxiDjibouti` (ancien MVP).
**Répertoire de travail :** `C:\prjRecherche\Taxi` (scaffold Aspire déjà créé : `Taxi.AppHost`, `Taxi.ServiceDefaults`, `Taxi.slnx`).

> **Portée de CE plan :** fondations + module Tarification uniquement. Modules Identité, Courses, Dispatch, Administration, temps réel + Blob, stubs Paiement/Notifications → plans ultérieurs.

---

## Structure de fichiers cible (à l'issue de ce plan)

```
C:\prjRecherche\Taxi\
├── Directory.Build.props              net10, nullable, implicit usings
├── Directory.Packages.props           gestion centralisée des versions
├── .gitignore
├── Taxi.slnx
├── Taxi.AppHost/                      (existant) + PostgreSQL + ref API
├── Taxi.ServiceDefaults/              (existant)
├── docs/superpowers/                  spec + ce plan (copiés)
├── src/
│   ├── Taxi.SharedKernel/             Result, Error, Entity, ICommand/IQuery(+Handler), IEndpoint
│   ├── Taxi.Domain/                   Pricing/ZonePrice.cs
│   ├── Taxi.Application/              Abstractions/ + Pricing/ (queries+handlers+validators) + DI
│   ├── Taxi.Infrastructure/           AppDbContext, Repository, Configurations/, DI
│   └── Taxi.Web.Api/                  Program.cs, endpoints, ResultExtensions, EndpointExtensions
└── tests/
    ├── Taxi.Architecture.Tests/       règles de dépendances (NetArchTest)
    └── Taxi.Application.Tests/        tests unitaires des handlers
```

**Dépendances entre projets :** `Web.Api → Infrastructure → Application → Domain → SharedKernel`.

---

## Task 1: Initialiser git et la gestion centralisée des packages

**Files:**
- Create: `C:\prjRecherche\Taxi\.gitignore`
- Create: `C:\prjRecherche\Taxi\Directory.Build.props`
- Create: `C:\prjRecherche\Taxi\Directory.Packages.props`

- [ ] **Step 1: Initialiser le dépôt git**

Run:
```bash
cd /c/prjRecherche/Taxi && git init && git branch -M main
```
Expected: `Initialized empty Git repository in C:/prjRecherche/Taxi/.git/`

- [ ] **Step 2: Créer le .gitignore .NET**

Run:
```bash
cd /c/prjRecherche/Taxi && dotnet new gitignore
```
Expected: `The template "dotnet gitignore file" was created successfully.`

- [ ] **Step 3: Créer `Directory.Build.props`**

```xml
<Project>
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <LangVersion>latest</LangVersion>
  </PropertyGroup>
</Project>
```

- [ ] **Step 4: Créer `Directory.Packages.props`**

```xml
<Project>
  <PropertyGroup>
    <ManagePackageVersionsCentrally>true</ManagePackageVersionsCentrally>
  </PropertyGroup>
  <ItemGroup>
    <!-- Aspire (aligné sur Aspire.AppHost.Sdk 13.1.0) -->
    <PackageVersion Include="Aspire.Hosting.PostgreSQL" Version="13.1.0" />
    <PackageVersion Include="Aspire.Npgsql.EntityFrameworkCore.PostgreSQL" Version="13.1.0" />
    <!-- Application -->
    <PackageVersion Include="Scrutor" Version="7.0.0" />
    <PackageVersion Include="FluentValidation.DependencyInjectionExtensions" Version="12.1.1" />
    <PackageVersion Include="Ardalis.Specification" Version="9.3.1" />
    <PackageVersion Include="Ardalis.Specification.EntityFrameworkCore" Version="9.3.1" />
    <!-- Infrastructure -->
    <PackageVersion Include="Microsoft.EntityFrameworkCore" Version="10.0.8" />
    <PackageVersion Include="Microsoft.EntityFrameworkCore.Design" Version="10.0.8" />
    <PackageVersion Include="Npgsql.EntityFrameworkCore.PostgreSQL" Version="10.0.2" />
    <PackageVersion Include="EFCore.NamingConventions" Version="10.0.1" />
    <!-- Web.Api -->
    <PackageVersion Include="Scalar.AspNetCore" Version="2.9.0" />
    <PackageVersion Include="Serilog.AspNetCore" Version="10.0.0" />
    <!-- Tests -->
    <PackageVersion Include="Microsoft.NET.Test.Sdk" Version="17.12.0" />
    <PackageVersion Include="xunit" Version="2.9.3" />
    <PackageVersion Include="xunit.runner.visualstudio" Version="3.1.5" />
    <PackageVersion Include="NetArchTest.Rules" Version="1.3.2" />
    <PackageVersion Include="FluentAssertions" Version="6.12.1" />
    <PackageVersion Include="Moq" Version="4.20.72" />
  </ItemGroup>
</Project>
```

> **Note exécutant :** si NuGet ne résout pas `13.1.0` pour les packages Aspire, lister les versions dispo avec `dotnet package search Aspire.Hosting.PostgreSQL --source nuget.org` et prendre la plus récente alignée sur le SDK AppHost. Ne PAS continuer avec une version qui ne restaure pas.

- [ ] **Step 5: Vérifier que la solution existante restaure et build encore**

Run:
```bash
cd /c/prjRecherche/Taxi && dotnet build Taxi.slnx
```
Expected: `Build succeeded.` (AppHost + ServiceDefaults compilent avec les nouveaux props).

- [ ] **Step 6: Copier le spec dans le nouveau repo**

Run:
```bash
mkdir -p /c/prjRecherche/Taxi/docs/superpowers/specs && cp "/c/prjRecherche/TaxiDjibouti/docs/superpowers/specs/2026-06-16-migration-net10-clean-archi-design.md" /c/prjRecherche/Taxi/docs/superpowers/specs/
```
Expected: pas de sortie (copie réussie).

- [ ] **Step 7: Commit**

```bash
cd /c/prjRecherche/Taxi && git add -A && git commit -m "chore: init repo, central package management, .NET 10 props"
```

---

## Task 2: Créer les 5 projets en couches et leurs références

**Files:**
- Create: `src/Taxi.SharedKernel/Taxi.SharedKernel.csproj`
- Create: `src/Taxi.Domain/Taxi.Domain.csproj`
- Create: `src/Taxi.Application/Taxi.Application.csproj`
- Create: `src/Taxi.Infrastructure/Taxi.Infrastructure.csproj`
- Create: `src/Taxi.Web.Api/Taxi.Web.Api.csproj`

- [ ] **Step 1: Créer les projets**

Run:
```bash
cd /c/prjRecherche/Taxi
dotnet new classlib -o src/Taxi.SharedKernel
dotnet new classlib -o src/Taxi.Domain
dotnet new classlib -o src/Taxi.Application
dotnet new classlib -o src/Taxi.Infrastructure
dotnet new web -o src/Taxi.Web.Api
```
Expected: 5 × `The template ... was created successfully.`

- [ ] **Step 2: Supprimer les fichiers Class1.cs générés**

Run:
```bash
cd /c/prjRecherche/Taxi && rm -f src/Taxi.SharedKernel/Class1.cs src/Taxi.Domain/Class1.cs src/Taxi.Application/Class1.cs src/Taxi.Infrastructure/Class1.cs
```
Expected: pas de sortie.

- [ ] **Step 3: Câbler les références entre projets (sens Clean Architecture)**

Run:
```bash
cd /c/prjRecherche/Taxi
dotnet add src/Taxi.Domain reference src/Taxi.SharedKernel
dotnet add src/Taxi.Application reference src/Taxi.Domain
dotnet add src/Taxi.Infrastructure reference src/Taxi.Application
dotnet add src/Taxi.Web.Api reference src/Taxi.Infrastructure
dotnet add src/Taxi.Web.Api reference Taxi.ServiceDefaults/Taxi.ServiceDefaults.csproj
```
Expected: `Reference ... added to the project.` ×5

- [ ] **Step 4: Ajouter les projets à la solution**

Run:
```bash
cd /c/prjRecherche/Taxi
dotnet sln Taxi.slnx add src/Taxi.SharedKernel src/Taxi.Domain src/Taxi.Application src/Taxi.Infrastructure src/Taxi.Web.Api
```
Expected: `Project ... added to the solution.` ×5

- [ ] **Step 5: Référencer l'API depuis l'AppHost**

Run:
```bash
cd /c/prjRecherche/Taxi && dotnet add Taxi.AppHost reference src/Taxi.Web.Api
```
Expected: `Reference ... added to the project.`

- [ ] **Step 6: Build**

Run:
```bash
cd /c/prjRecherche/Taxi && dotnet build Taxi.slnx
```
Expected: `Build succeeded.`

- [ ] **Step 7: Commit**

```bash
cd /c/prjRecherche/Taxi && git add -A && git commit -m "chore: scaffold layered projects (SharedKernel/Domain/Application/Infrastructure/Web.Api)"
```

---

## Task 3: SharedKernel — Result et Error (TDD)

**Files:**
- Create: `src/Taxi.SharedKernel/Error.cs`
- Create: `src/Taxi.SharedKernel/Result.cs`
- Create: `tests/Taxi.Application.Tests/Taxi.Application.Tests.csproj` (créé ici, réutilisé ensuite)
- Test: `tests/Taxi.Application.Tests/SharedKernel/ResultTests.cs`

- [ ] **Step 1: Créer le projet de tests et le câbler**

Run:
```bash
cd /c/prjRecherche/Taxi
dotnet new xunit -o tests/Taxi.Application.Tests
rm -f tests/Taxi.Application.Tests/UnitTest1.cs
dotnet add tests/Taxi.Application.Tests reference src/Taxi.Application
dotnet add tests/Taxi.Application.Tests package FluentAssertions
dotnet sln Taxi.slnx add tests/Taxi.Application.Tests
```
Expected: succès de chaque commande.

- [ ] **Step 2: Écrire le test qui échoue**

Create `tests/Taxi.Application.Tests/SharedKernel/ResultTests.cs`:
```csharp
using FluentAssertions;
using Taxi.SharedKernel;
using Xunit;

namespace Taxi.Application.Tests.SharedKernel;

public class ResultTests
{
    [Fact]
    public void Success_should_be_success_with_no_error()
    {
        var result = Result.Success();
        result.IsSuccess.Should().BeTrue();
        result.Error.Should().Be(Error.None);
    }

    [Fact]
    public void Failure_should_carry_the_error()
    {
        var error = Error.NotFound("X.NotFound", "Introuvable");
        var result = Result.Failure(error);
        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Be(error);
    }

    [Fact]
    public void Generic_success_should_expose_value()
    {
        Result<int> result = 42;
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(42);
    }

    [Fact]
    public void Accessing_value_on_failure_should_throw()
    {
        var result = Result.Failure<int>(Error.Validation("X", "bad"));
        var act = () => result.Value;
        act.Should().Throw<InvalidOperationException>();
    }
}
```

- [ ] **Step 3: Lancer le test pour vérifier qu'il échoue (compilation)**

Run:
```bash
cd /c/prjRecherche/Taxi && dotnet test tests/Taxi.Application.Tests
```
Expected: ÉCHEC de compilation (`Error`/`Result` n'existent pas).

- [ ] **Step 4: Implémenter `Error.cs`**

```csharp
namespace Taxi.SharedKernel;

public enum ErrorType { Failure = 0, Validation = 1, NotFound = 2, Conflict = 3 }

public sealed record Error(string Code, string Description, ErrorType Type)
{
    public static readonly Error None = new(string.Empty, string.Empty, ErrorType.Failure);

    public static Error Failure(string code, string description) => new(code, description, ErrorType.Failure);
    public static Error Validation(string code, string description) => new(code, description, ErrorType.Validation);
    public static Error NotFound(string code, string description) => new(code, description, ErrorType.NotFound);
    public static Error Conflict(string code, string description) => new(code, description, ErrorType.Conflict);
}
```

- [ ] **Step 5: Implémenter `Result.cs`**

```csharp
namespace Taxi.SharedKernel;

public class Result
{
    protected Result(bool isSuccess, Error error)
    {
        if (isSuccess && error != Error.None)
            throw new InvalidOperationException("Un succès ne peut pas porter d'erreur.");
        if (!isSuccess && error == Error.None)
            throw new InvalidOperationException("Un échec doit porter une erreur.");

        IsSuccess = isSuccess;
        Error = error;
    }

    public bool IsSuccess { get; }
    public bool IsFailure => !IsSuccess;
    public Error Error { get; }

    public static Result Success() => new(true, Error.None);
    public static Result Failure(Error error) => new(false, error);
    public static Result<TValue> Success<TValue>(TValue value) => new(value, true, Error.None);
    public static Result<TValue> Failure<TValue>(Error error) => new(default, false, error);
}

public class Result<TValue> : Result
{
    private readonly TValue? _value;

    protected internal Result(TValue? value, bool isSuccess, Error error) : base(isSuccess, error)
        => _value = value;

    public TValue Value => IsSuccess
        ? _value!
        : throw new InvalidOperationException("On ne peut pas lire la valeur d'un résultat en échec.");

    public static implicit operator Result<TValue>(TValue value) => Success(value);
}
```

- [ ] **Step 6: Lancer les tests — doivent passer**

Run:
```bash
cd /c/prjRecherche/Taxi && dotnet test tests/Taxi.Application.Tests
```
Expected: `Passed! - Failed: 0, Passed: 4`.

- [ ] **Step 7: Commit**

```bash
cd /c/prjRecherche/Taxi && git add -A && git commit -m "feat(sharedkernel): add Result and Error with tests"
```

---

## Task 4: SharedKernel — Entity, abstractions CQRS et IEndpoint

**Files:**
- Create: `src/Taxi.SharedKernel/Entity.cs`
- Create: `src/Taxi.SharedKernel/Messaging/ICommand.cs`
- Create: `src/Taxi.SharedKernel/Messaging/IQuery.cs`
- Create: `src/Taxi.SharedKernel/Messaging/ICommandHandler.cs`
- Create: `src/Taxi.SharedKernel/Messaging/IQueryHandler.cs`

- [ ] **Step 1: Créer `Entity.cs` (base des entités, clé int auto-incrémentée comme l'existant)**

```csharp
namespace Taxi.SharedKernel;

public abstract class Entity
{
    public int Id { get; protected set; }
    public DateTime CreatedAt { get; protected set; } = DateTime.UtcNow;
}
```

- [ ] **Step 2: Créer les marqueurs `ICommand` / `IQuery`**

`src/Taxi.SharedKernel/Messaging/ICommand.cs`:
```csharp
namespace Taxi.SharedKernel.Messaging;

public interface ICommand<TResponse>;
```

`src/Taxi.SharedKernel/Messaging/IQuery.cs`:
```csharp
namespace Taxi.SharedKernel.Messaging;

public interface IQuery<TResponse>;
```

- [ ] **Step 3: Créer les handlers**

`src/Taxi.SharedKernel/Messaging/ICommandHandler.cs`:
```csharp
namespace Taxi.SharedKernel.Messaging;

public interface ICommandHandler<in TCommand, TResponse>
    where TCommand : ICommand<TResponse>
{
    Task<Result<TResponse>> Handle(TCommand command, CancellationToken cancellationToken);
}
```

`src/Taxi.SharedKernel/Messaging/IQueryHandler.cs`:
```csharp
namespace Taxi.SharedKernel.Messaging;

public interface IQueryHandler<in TQuery, TResponse>
    where TQuery : IQuery<TResponse>
{
    Task<Result<TResponse>> Handle(TQuery query, CancellationToken cancellationToken);
}
```

- [ ] **Step 4: Build**

Run:
```bash
cd /c/prjRecherche/Taxi && dotnet build src/Taxi.SharedKernel
```
Expected: `Build succeeded.`

- [ ] **Step 5: Commit**

```bash
cd /c/prjRecherche/Taxi && git add -A && git commit -m "feat(sharedkernel): add Entity base and CQRS messaging abstractions"
```

---

## Task 5: Application — abstractions repository + décorateurs Scrutor + DI

**Files:**
- Create: `src/Taxi.Application/Abstractions/IRepository.cs`
- Create: `src/Taxi.Application/Abstractions/Behaviors/ValidationDecorator.cs`
- Create: `src/Taxi.Application/Abstractions/Behaviors/LoggingDecorator.cs`
- Create: `src/Taxi.Application/DependencyInjection.cs`
- Test: `tests/Taxi.Application.Tests/Behaviors/ValidationDecoratorTests.cs`

- [ ] **Step 1: Ajouter les packages à Application**

Run:
```bash
cd /c/prjRecherche/Taxi
dotnet add src/Taxi.Application package Scrutor
dotnet add src/Taxi.Application package FluentValidation.DependencyInjectionExtensions
dotnet add src/Taxi.Application package Ardalis.Specification
dotnet add src/Taxi.Application package Microsoft.Extensions.Logging.Abstractions
```
Expected: succès.

- [ ] **Step 2: Créer `IRepository.cs` (générique, basé sur Ardalis.Specification)**

```csharp
using Ardalis.Specification;
using Taxi.SharedKernel;

namespace Taxi.Application.Abstractions;

public interface IRepository<T> : IRepositoryBase<T> where T : Entity;
```

- [ ] **Step 3: Écrire le test du décorateur de validation (échoue d'abord)**

`tests/Taxi.Application.Tests/Behaviors/ValidationDecoratorTests.cs`:
```csharp
using FluentAssertions;
using FluentValidation;
using Taxi.Application.Abstractions.Behaviors;
using Taxi.SharedKernel;
using Taxi.SharedKernel.Messaging;
using Xunit;

namespace Taxi.Application.Tests.Behaviors;

public class ValidationDecoratorTests
{
    public sealed record DummyCommand(int Amount) : ICommand<int>;

    public sealed class DummyValidator : AbstractValidator<DummyCommand>
    {
        public DummyValidator() => RuleFor(c => c.Amount).GreaterThan(0);
    }

    private sealed class PassThroughHandler : ICommandHandler<DummyCommand, int>
    {
        public Task<Result<int>> Handle(DummyCommand command, CancellationToken ct)
            => Task.FromResult(Result.Success(command.Amount));
    }

    [Fact]
    public async Task Should_return_validation_failure_when_invalid()
    {
        var decorator = new ValidationDecorator.CommandHandler<DummyCommand, int>(
            new PassThroughHandler(), new IValidator<DummyCommand>[] { new DummyValidator() });

        var result = await decorator.Handle(new DummyCommand(0), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Type.Should().Be(ErrorType.Validation);
    }

    [Fact]
    public async Task Should_call_inner_handler_when_valid()
    {
        var decorator = new ValidationDecorator.CommandHandler<DummyCommand, int>(
            new PassThroughHandler(), new IValidator<DummyCommand>[] { new DummyValidator() });

        var result = await decorator.Handle(new DummyCommand(5), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(5);
    }
}
```

- [ ] **Step 4: Lancer le test → échec de compilation (décorateur absent)**

Run: `cd /c/prjRecherche/Taxi && dotnet test tests/Taxi.Application.Tests`
Expected: ÉCHEC de compilation.

- [ ] **Step 5: Implémenter `ValidationDecorator.cs`**

```csharp
using FluentValidation;
using Taxi.SharedKernel;
using Taxi.SharedKernel.Messaging;

namespace Taxi.Application.Abstractions.Behaviors;

public static class ValidationDecorator
{
    public sealed class CommandHandler<TCommand, TResponse>(
        ICommandHandler<TCommand, TResponse> inner,
        IEnumerable<IValidator<TCommand>> validators)
        : ICommandHandler<TCommand, TResponse>
        where TCommand : ICommand<TResponse>
    {
        public async Task<Result<TResponse>> Handle(TCommand command, CancellationToken cancellationToken)
        {
            var failure = Validate(command, validators);
            return failure is null
                ? await inner.Handle(command, cancellationToken)
                : Result.Failure<TResponse>(failure);
        }
    }

    public sealed class QueryHandler<TQuery, TResponse>(
        IQueryHandler<TQuery, TResponse> inner,
        IEnumerable<IValidator<TQuery>> validators)
        : IQueryHandler<TQuery, TResponse>
        where TQuery : IQuery<TResponse>
    {
        public async Task<Result<TResponse>> Handle(TQuery query, CancellationToken cancellationToken)
        {
            var failure = Validate(query, validators);
            return failure is null
                ? await inner.Handle(query, cancellationToken)
                : Result.Failure<TResponse>(failure);
        }
    }

    private static Error? Validate<T>(T request, IEnumerable<IValidator<T>> validators)
    {
        var context = new ValidationContext<T>(request);
        var firstError = validators
            .Select(v => v.Validate(context))
            .SelectMany(r => r.Errors)
            .FirstOrDefault();

        return firstError is null
            ? null
            : Error.Validation(firstError.PropertyName, firstError.ErrorMessage);
    }
}
```

- [ ] **Step 6: Lancer les tests → passent**

Run: `cd /c/prjRecherche/Taxi && dotnet test tests/Taxi.Application.Tests`
Expected: `Passed!` (6 tests au total).

- [ ] **Step 7: Implémenter `LoggingDecorator.cs`**

```csharp
using Microsoft.Extensions.Logging;
using Taxi.SharedKernel;
using Taxi.SharedKernel.Messaging;

namespace Taxi.Application.Abstractions.Behaviors;

public static class LoggingDecorator
{
    public sealed class CommandHandler<TCommand, TResponse>(
        ICommandHandler<TCommand, TResponse> inner,
        ILogger<CommandHandler<TCommand, TResponse>> logger)
        : ICommandHandler<TCommand, TResponse>
        where TCommand : ICommand<TResponse>
    {
        public async Task<Result<TResponse>> Handle(TCommand command, CancellationToken cancellationToken)
        {
            var name = typeof(TCommand).Name;
            logger.LogInformation("Traitement de la commande {Command}", name);
            var result = await inner.Handle(command, cancellationToken);
            if (result.IsSuccess)
                logger.LogInformation("Commande {Command} traitée avec succès", name);
            else
                logger.LogWarning("Commande {Command} en échec : {Error}", name, result.Error.Code);
            return result;
        }
    }
}
```

- [ ] **Step 8: Implémenter `DependencyInjection.cs` (scan handlers + validators + décorateurs)**

```csharp
using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using Taxi.Application.Abstractions.Behaviors;
using Taxi.SharedKernel.Messaging;

namespace Taxi.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        var assembly = typeof(DependencyInjection).Assembly;

        services.Scan(scan => scan.FromAssemblies(assembly)
            .AddClasses(c => c.AssignableTo(typeof(ICommandHandler<,>)), publicOnly: false)
                .AsImplementedInterfaces().WithScopedLifetime()
            .AddClasses(c => c.AssignableTo(typeof(IQueryHandler<,>)), publicOnly: false)
                .AsImplementedInterfaces().WithScopedLifetime());

        services.AddValidatorsFromAssembly(assembly, includeInternalTypes: true);

        // Décorateurs : validation au plus près du handler, logging au-dessus.
        services.Decorate(typeof(ICommandHandler<,>), typeof(ValidationDecorator.CommandHandler<,>));
        services.Decorate(typeof(IQueryHandler<,>), typeof(ValidationDecorator.QueryHandler<,>));
        services.Decorate(typeof(ICommandHandler<,>), typeof(LoggingDecorator.CommandHandler<,>));

        return services;
    }
}
```

- [ ] **Step 9: Build + tests**

Run: `cd /c/prjRecherche/Taxi && dotnet build Taxi.slnx && dotnet test tests/Taxi.Application.Tests`
Expected: build OK, tests `Passed!`.

- [ ] **Step 10: Commit**

```bash
cd /c/prjRecherche/Taxi && git add -A && git commit -m "feat(application): repository abstraction, Scrutor DI, validation+logging decorators"
```

---

## Task 6: Domain + Infrastructure — entité Pricing, DbContext, repository, EF config

**Files:**
- Create: `src/Taxi.Domain/Pricing/ZonePrice.cs`
- Create: `src/Taxi.Infrastructure/Persistence/AppDbContext.cs`
- Create: `src/Taxi.Infrastructure/Persistence/Repository.cs`
- Create: `src/Taxi.Infrastructure/Persistence/Configurations/ZonePriceConfiguration.cs`
- Create: `src/Taxi.Infrastructure/DependencyInjection.cs`

- [ ] **Step 1: Ajouter les packages à Infrastructure**

Run:
```bash
cd /c/prjRecherche/Taxi
dotnet add src/Taxi.Infrastructure package Microsoft.EntityFrameworkCore
dotnet add src/Taxi.Infrastructure package Npgsql.EntityFrameworkCore.PostgreSQL
dotnet add src/Taxi.Infrastructure package EFCore.NamingConventions
dotnet add src/Taxi.Infrastructure package Ardalis.Specification.EntityFrameworkCore
dotnet add src/Taxi.Infrastructure package Aspire.Npgsql.EntityFrameworkCore.PostgreSQL
```
Expected: succès.

- [ ] **Step 2: Créer l'entité `ZonePrice` (riche : factory + règle métier du prix)**

```csharp
using Taxi.SharedKernel;

namespace Taxi.Domain.Pricing;

public sealed class ZonePrice : Entity
{
    public const decimal DefaultPrice = 1000m;

    public string FromZone { get; private set; } = string.Empty;
    public string ToZone { get; private set; } = string.Empty;
    public decimal Price { get; private set; }

    private ZonePrice() { } // EF

    public static ZonePrice Create(string fromZone, string toZone, decimal price)
        => new() { FromZone = fromZone, ToZone = toZone, Price = price };
}
```

- [ ] **Step 3: Créer `AppDbContext`**

```csharp
using Microsoft.EntityFrameworkCore;
using Taxi.Domain.Pricing;

namespace Taxi.Infrastructure.Persistence;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<ZonePrice> ZonePrices => Set<ZonePrice>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
        => modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
}
```

- [ ] **Step 4: Créer la configuration EF de `ZonePrice`**

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Taxi.Domain.Pricing;

namespace Taxi.Infrastructure.Persistence.Configurations;

internal sealed class ZonePriceConfiguration : IEntityTypeConfiguration<ZonePrice>
{
    public void Configure(EntityTypeBuilder<ZonePrice> builder)
    {
        builder.ToTable("zone_prices");
        builder.HasKey(z => z.Id);
        builder.Property(z => z.FromZone).HasMaxLength(100).IsRequired();
        builder.Property(z => z.ToZone).HasMaxLength(100).IsRequired();
        builder.Property(z => z.Price).HasColumnType("numeric(10,2)");
        builder.HasIndex(z => new { z.FromZone, z.ToZone }).IsUnique();
    }
}
```

- [ ] **Step 5: Créer le `Repository` générique (Ardalis)**

```csharp
using Ardalis.Specification.EntityFrameworkCore;
using Taxi.Application.Abstractions;
using Taxi.SharedKernel;

namespace Taxi.Infrastructure.Persistence;

public sealed class Repository<T>(AppDbContext context)
    : RepositoryBase<T>(context), IRepository<T> where T : Entity;
```

- [ ] **Step 6: Créer `DependencyInjection.cs` (Infrastructure)**

```csharp
using Microsoft.Extensions.DependencyInjection;
using Taxi.Application.Abstractions;
using Taxi.Infrastructure.Persistence;

namespace Taxi.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services)
    {
        services.AddScoped(typeof(IRepository<>), typeof(Repository<>));
        return services;
    }
}
```

> Note : l'enregistrement de `AppDbContext` se fait côté Web.Api via Aspire (`AddNpgsqlDbContext`), Task 8.

- [ ] **Step 7: Build**

Run: `cd /c/prjRecherche/Taxi && dotnet build Taxi.slnx`
Expected: `Build succeeded.`

- [ ] **Step 8: Commit**

```bash
cd /c/prjRecherche/Taxi && git add -A && git commit -m "feat(infra): ZonePrice entity, AppDbContext, EF config, generic repository"
```

---

## Task 7: Application Pricing — Query d'estimation de prix (TDD)

**Files:**
- Create: `src/Taxi.Application/Pricing/EstimatePrice/EstimatePriceQuery.cs`
- Create: `src/Taxi.Application/Pricing/EstimatePrice/EstimatePriceResponse.cs`
- Create: `src/Taxi.Application/Pricing/EstimatePrice/EstimatePriceQueryValidator.cs`
- Create: `src/Taxi.Application/Pricing/EstimatePrice/ZonePriceByZonesSpec.cs`
- Create: `src/Taxi.Application/Pricing/EstimatePrice/EstimatePriceQueryHandler.cs`
- Test: `tests/Taxi.Application.Tests/Pricing/EstimatePriceQueryHandlerTests.cs`

Comportement de référence (ancien `RideService.GetEstimatedPriceAsync`) : on cherche un `ZonePrice` pour (FromZone, ToZone) ; si trouvé → son prix ; sinon → `ZonePrice.DefaultPrice` (1000).

- [ ] **Step 1: Créer la query, la réponse, le validator**

`EstimatePriceQuery.cs`:
```csharp
using Taxi.SharedKernel.Messaging;

namespace Taxi.Application.Pricing.EstimatePrice;

public sealed record EstimatePriceQuery(string FromZone, string ToZone)
    : IQuery<EstimatePriceResponse>;
```

`EstimatePriceResponse.cs`:
```csharp
namespace Taxi.Application.Pricing.EstimatePrice;

public sealed record EstimatePriceResponse(string FromZone, string ToZone, decimal Price);
```

`EstimatePriceQueryValidator.cs`:
```csharp
using FluentValidation;

namespace Taxi.Application.Pricing.EstimatePrice;

internal sealed class EstimatePriceQueryValidator : AbstractValidator<EstimatePriceQuery>
{
    public EstimatePriceQueryValidator()
    {
        RuleFor(q => q.FromZone).NotEmpty();
        RuleFor(q => q.ToZone).NotEmpty();
    }
}
```

- [ ] **Step 2: Créer la Specification**

`ZonePriceByZonesSpec.cs`:
```csharp
using Ardalis.Specification;
using Taxi.Domain.Pricing;

namespace Taxi.Application.Pricing.EstimatePrice;

public sealed class ZonePriceByZonesSpec : Specification<ZonePrice>
{
    public ZonePriceByZonesSpec(string fromZone, string toZone)
        => Query.Where(z => z.FromZone == fromZone && z.ToZone == toZone);
}
```

- [ ] **Step 3: Écrire le test du handler (échoue d'abord)**

`tests/Taxi.Application.Tests/Pricing/EstimatePriceQueryHandlerTests.cs`:
```csharp
using Ardalis.Specification;
using FluentAssertions;
using Moq;
using Taxi.Application.Abstractions;
using Taxi.Application.Pricing.EstimatePrice;
using Taxi.Domain.Pricing;
using Xunit;

namespace Taxi.Application.Tests.Pricing;

public class EstimatePriceQueryHandlerTests
{
    private readonly Mock<IRepository<ZonePrice>> _repo = new();

    [Fact]
    public async Task Should_return_zone_price_when_found()
    {
        var zp = ZonePrice.Create("Centre-ville", "Balbala", 1500m);
        _repo.Setup(r => r.FirstOrDefaultAsync(It.IsAny<ISpecification<ZonePrice>>(), It.IsAny<CancellationToken>()))
             .ReturnsAsync(zp);
        var handler = new EstimatePriceQueryHandler(_repo.Object);

        var result = await handler.Handle(new EstimatePriceQuery("Centre-ville", "Balbala"), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Price.Should().Be(1500m);
    }

    [Fact]
    public async Task Should_return_default_price_when_not_found()
    {
        _repo.Setup(r => r.FirstOrDefaultAsync(It.IsAny<ISpecification<ZonePrice>>(), It.IsAny<CancellationToken>()))
             .ReturnsAsync((ZonePrice?)null);
        var handler = new EstimatePriceQueryHandler(_repo.Object);

        var result = await handler.Handle(new EstimatePriceQuery("X", "Y"), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Price.Should().Be(ZonePrice.DefaultPrice);
    }
}
```

- [ ] **Step 4: Ajouter Moq au projet de tests** (version gérée centralement, déjà dans `Directory.Packages.props`)

Run:
```bash
cd /c/prjRecherche/Taxi && dotnet add tests/Taxi.Application.Tests package Moq
```
Expected: succès.

- [ ] **Step 5: Lancer le test → échec de compilation (handler absent)**

Run: `cd /c/prjRecherche/Taxi && dotnet test tests/Taxi.Application.Tests`
Expected: ÉCHEC de compilation.

- [ ] **Step 6: Implémenter le handler**

`EstimatePriceQueryHandler.cs`:
```csharp
using Taxi.Application.Abstractions;
using Taxi.Domain.Pricing;
using Taxi.SharedKernel;
using Taxi.SharedKernel.Messaging;

namespace Taxi.Application.Pricing.EstimatePrice;

internal sealed class EstimatePriceQueryHandler(IRepository<ZonePrice> repository)
    : IQueryHandler<EstimatePriceQuery, EstimatePriceResponse>
{
    public async Task<Result<EstimatePriceResponse>> Handle(
        EstimatePriceQuery query, CancellationToken cancellationToken)
    {
        var match = await repository.FirstOrDefaultAsync(
            new ZonePriceByZonesSpec(query.FromZone, query.ToZone), cancellationToken);

        var price = match?.Price ?? ZonePrice.DefaultPrice;
        return new EstimatePriceResponse(query.FromZone, query.ToZone, price);
    }
}
```

- [ ] **Step 7: Lancer les tests → passent**

Run: `cd /c/prjRecherche/Taxi && dotnet test tests/Taxi.Application.Tests`
Expected: `Passed!` (8 tests au total).

- [ ] **Step 8: Commit**

```bash
cd /c/prjRecherche/Taxi && git add -A && git commit -m "feat(pricing): EstimatePrice query handler with specification (TDD)"
```

---

## Task 8: Web.Api — hôte, Aspire, Scalar, endpoint Pricing, Result→HTTP

**Files:**
- Create: `src/Taxi.Web.Api/Endpoints/IEndpoint.cs`
- Create: `src/Taxi.Web.Api/Endpoints/EndpointExtensions.cs`
- Create: `src/Taxi.Web.Api/Endpoints/ResultExtensions.cs`
- Create: `src/Taxi.Web.Api/Modules/Pricing/EstimatePriceEndpoint.cs`
- Modify: `src/Taxi.Web.Api/Program.cs` (remplacement complet)
- Modify: `Taxi.AppHost/AppHost.cs` (remplacement complet)

- [ ] **Step 1: Ajouter les packages à Web.Api**

Run:
```bash
cd /c/prjRecherche/Taxi
dotnet add src/Taxi.Web.Api package Scalar.AspNetCore
dotnet add src/Taxi.Web.Api package Aspire.Npgsql.EntityFrameworkCore.PostgreSQL
dotnet add src/Taxi.Web.Api package Microsoft.AspNetCore.OpenApi
```
Expected: succès.

- [ ] **Step 2: Créer l'abstraction `IEndpoint` + l'enregistrement**

`Endpoints/IEndpoint.cs`:
```csharp
namespace Taxi.Web.Api.Endpoints;

public interface IEndpoint
{
    void MapEndpoint(IEndpointRouteBuilder app);
}
```

`Endpoints/EndpointExtensions.cs`:
```csharp
using System.Reflection;

namespace Taxi.Web.Api.Endpoints;

public static class EndpointExtensions
{
    public static IServiceCollection AddEndpoints(this IServiceCollection services)
    {
        var endpoints = Assembly.GetExecutingAssembly().DefinedTypes
            .Where(t => t is { IsAbstract: false, IsInterface: false } && typeof(IEndpoint).IsAssignableFrom(t))
            .Select(t => ServiceDescriptor.Transient(typeof(IEndpoint), t));

        services.TryAddEnumerableRange(endpoints);
        return services;
    }

    public static IApplicationBuilder MapEndpoints(this WebApplication app)
    {
        foreach (var endpoint in app.Services.GetRequiredService<IEnumerable<IEndpoint>>())
            endpoint.MapEndpoint(app);
        return app;
    }

    private static void TryAddEnumerableRange(this IServiceCollection services, IEnumerable<ServiceDescriptor> descriptors)
    {
        foreach (var d in descriptors) services.Add(d);
    }
}
```

- [ ] **Step 3: Créer `ResultExtensions` (Result → IResult / ProblemDetails)**

`Endpoints/ResultExtensions.cs`:
```csharp
using Taxi.SharedKernel;

namespace Taxi.Web.Api.Endpoints;

public static class ResultExtensions
{
    public static IResult ToHttpResult<T>(this Result<T> result)
        => result.IsSuccess ? Results.Ok(result.Value) : Problem(result.Error);

    private static IResult Problem(Error error)
    {
        var status = error.Type switch
        {
            ErrorType.NotFound => StatusCodes.Status404NotFound,
            ErrorType.Validation => StatusCodes.Status400BadRequest,
            ErrorType.Conflict => StatusCodes.Status409Conflict,
            _ => StatusCodes.Status400BadRequest
        };
        return Results.Problem(statusCode: status, title: error.Code, detail: error.Description);
    }
}
```

- [ ] **Step 4: Créer l'endpoint Pricing**

`Modules/Pricing/EstimatePriceEndpoint.cs`:
```csharp
using Taxi.Application.Pricing.EstimatePrice;
using Taxi.SharedKernel.Messaging;
using Taxi.Web.Api.Endpoints;

namespace Taxi.Web.Api.Modules.Pricing;

public sealed class EstimatePriceEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapGet("/api/pricing/estimate", async (
            string fromZone, string toZone,
            IQueryHandler<EstimatePriceQuery, EstimatePriceResponse> handler,
            CancellationToken ct) =>
        {
            var result = await handler.Handle(new EstimatePriceQuery(fromZone, toZone), ct);
            return result.ToHttpResult();
        })
        .WithTags("Pricing");
    }
}
```

- [ ] **Step 5: Remplacer `Program.cs`**

```csharp
using Scalar.AspNetCore;
using Taxi.Application;
using Taxi.Infrastructure;
using Taxi.Infrastructure.Persistence;
using Taxi.Web.Api.Endpoints;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.Services.AddOpenApi();

builder.AddNpgsqlDbContext<AppDbContext>("taxidb");

builder.Services.AddApplication();
builder.Services.AddInfrastructure();
builder.Services.AddEndpoints();

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    db.Database.EnsureCreated();
}

app.MapDefaultEndpoints();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference();
}

app.MapEndpoints();

app.Run();
```

> Note : `EnsureCreated()` est temporaire (pas de migrations dans ce plan de fondation). Les migrations EF seront ajoutées au plan du module Identité (premier module avec schéma stable).

- [ ] **Step 6: Remplacer `Taxi.AppHost/AppHost.cs` (ajout PostgreSQL + API)**

```csharp
var builder = DistributedApplication.CreateBuilder(args);

var postgres = builder.AddPostgres("postgres")
    .WithDataVolume();

var taxidb = postgres.AddDatabase("taxidb");

builder.AddProject<Projects.Taxi_Web_Api>("api")
    .WithReference(taxidb)
    .WaitFor(taxidb);

builder.Build().Run();
```

- [ ] **Step 7: Ajouter le package PostgreSQL hosting à l'AppHost**

Run:
```bash
cd /c/prjRecherche/Taxi && dotnet add Taxi.AppHost package Aspire.Hosting.PostgreSQL
```
Expected: succès.

- [ ] **Step 8: Build complet**

Run: `cd /c/prjRecherche/Taxi && dotnet build Taxi.slnx`
Expected: `Build succeeded.` (le type généré `Projects.Taxi_Web_Api` est résolu via la référence AppHost→API).

- [ ] **Step 9: Commit**

```bash
cd /c/prjRecherche/Taxi && git add -A && git commit -m "feat(api): Aspire+Postgres wiring, IEndpoint pattern, Scalar, Pricing endpoint"
```

---

## Task 9: Vérification manuelle de bout en bout (Aspire)

- [ ] **Step 1: Démarrer l'AppHost**

> Commande interactive longue. À lancer par l'utilisateur dans un terminal dédié :
```bash
dotnet run --project /c/prjRecherche/Taxi/Taxi.AppHost
```
Expected: le dashboard Aspire s'ouvre ; ressources `postgres` et `api` passent en `Running`.

- [ ] **Step 2: Tester l'endpoint via Scalar**

Ouvrir l'URL HTTPS du service `api` (dashboard) + `/scalar/v1`. Exécuter `GET /api/pricing/estimate?fromZone=X&toZone=Y`.
Expected : `200 OK`, corps `{ "fromZone": "X", "toZone": "Y", "price": 1000 }` (prix par défaut, table vide).

- [ ] **Step 3: Tester la validation**

Exécuter `GET /api/pricing/estimate?fromZone=&toZone=Y`.
Expected : `400 Bad Request` (le décorateur de validation a rejeté `FromZone` vide).

- [ ] **Step 4: Arrêter l'AppHost** (Ctrl+C) une fois vérifié.

> Aucun commit (vérification seule).

---

## Task 10: Tests d'architecture (NetArchTest)

**Files:**
- Create: `tests/Taxi.Architecture.Tests/Taxi.Architecture.Tests.csproj`
- Create: `tests/Taxi.Architecture.Tests/LayeringTests.cs`

- [ ] **Step 1: Créer le projet et le câbler**

Run:
```bash
cd /c/prjRecherche/Taxi
dotnet new xunit -o tests/Taxi.Architecture.Tests
rm -f tests/Taxi.Architecture.Tests/UnitTest1.cs
dotnet add tests/Taxi.Architecture.Tests package NetArchTest.Rules
dotnet add tests/Taxi.Architecture.Tests reference src/Taxi.Domain src/Taxi.Application src/Taxi.Infrastructure
dotnet sln Taxi.slnx add tests/Taxi.Architecture.Tests
```
Expected: succès.

- [ ] **Step 2: Écrire les règles de dépendances**

`LayeringTests.cs`:
```csharp
using FluentAssertions;
using NetArchTest.Rules;
using Xunit;

namespace Taxi.Architecture.Tests;

public class LayeringTests
{
    private const string Domain = "Taxi.Domain";
    private const string Application = "Taxi.Application";
    private const string Infrastructure = "Taxi.Infrastructure";

    [Fact]
    public void Domain_should_not_depend_on_Application_or_Infrastructure()
    {
        var result = Types.InAssembly(typeof(Domain.Pricing.ZonePrice).Assembly)
            .ShouldNot().HaveDependencyOnAny(Application, Infrastructure)
            .GetResult();

        result.IsSuccessful.Should().BeTrue();
    }

    [Fact]
    public void Application_should_not_depend_on_Infrastructure()
    {
        var result = Types.InAssembly(typeof(Application.DependencyInjection).Assembly)
            .ShouldNot().HaveDependencyOn(Infrastructure)
            .GetResult();

        result.IsSuccessful.Should().BeTrue();
    }
}
```

- [ ] **Step 3: Ajouter FluentAssertions et lancer les tests**

Run:
```bash
cd /c/prjRecherche/Taxi
dotnet add tests/Taxi.Architecture.Tests package FluentAssertions
dotnet test tests/Taxi.Architecture.Tests
```
Expected: `Passed! - Failed: 0, Passed: 2`.

- [ ] **Step 4: Lancer TOUTE la suite de tests**

Run: `cd /c/prjRecherche/Taxi && dotnet test Taxi.slnx`
Expected: tous les tests passent.

- [ ] **Step 5: Commit**

```bash
cd /c/prjRecherche/Taxi && git add -A && git commit -m "test(arch): enforce Clean Architecture dependency rules"
```

---

## Definition of Done (ce plan)

- [ ] `dotnet build Taxi.slnx` réussit.
- [ ] `dotnet test Taxi.slnx` : tous les tests verts (Result, décorateur, handler Pricing, archi).
- [ ] L'AppHost démarre ; `GET /api/pricing/estimate` répond `200` (prix par défaut 1000) et `400` sur entrée invalide.
- [ ] Les patterns sont posés : Result, ICommand/IQuery+Handler, Scrutor+décorateurs, IEndpoint, Repository+Specification, EF config, tests d'archi.
- [ ] Tout est commité sur `main` dans `C:\prjRecherche\Taxi`.

## Plans suivants (hors périmètre)

1. **Module Identité** : ASP.NET Identity + JWT + migrations EF + entité `DriverDocument` + Azure Blob.
2. **Module Courses** : `Ride`, cycle de vie (commands), Rating/Report, queries.
3. **Module Dispatch** : disponibilité chauffeur, courses en attente, acceptation.
4. **Module Administration** : stats + listes (queries).
5. **Temps réel** : `RideHub` SignalR derrière `IRideRealtimeNotifier`.
6. **Stubs** Paiement / Notifications.
