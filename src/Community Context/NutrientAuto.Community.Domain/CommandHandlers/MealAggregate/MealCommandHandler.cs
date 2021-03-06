﻿using AutoMapper;
using MediatR;
using Microsoft.Extensions.Logging;
using NutrientAuto.Community.Domain.Aggregates.FoodAggregate;
using NutrientAuto.Community.Domain.Aggregates.MealAggregate;
using NutrientAuto.Community.Domain.Commands.MealAggregate;
using NutrientAuto.Community.Domain.Context;
using NutrientAuto.Community.Domain.DomainEvents.MealAggregate;
using NutrientAuto.Community.Domain.Repositories.FoodAggregate;
using NutrientAuto.Community.Domain.Repositories.MealAggregate;
using NutrientAuto.CrossCutting.HttpService.HttpContext;
using NutrientAuto.CrossCutting.UnitOfwork.Abstractions;
using NutrientAuto.Shared.Commands;
using NutrientAuto.Shared.ValueObjects;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace NutrientAuto.Community.Domain.CommandHandlers.MealAggregate
{
    public class MealCommandHandler : ContextCommandHandler,
                                      IRequestHandler<UpdateMealCommand, CommandResult>,
                                      IRequestHandler<AddMealFoodCommand, CommandResult>,
                                      IRequestHandler<RemoveMealFoodCommand, CommandResult>
    {
        private readonly IMealRepository _mealRepository;
        private readonly IFoodRepository _foodRepository;
        private readonly IMapper _mapper;
        private readonly Guid _currentProfileId;

        public MealCommandHandler(IMealRepository mealRepository, IFoodRepository foodRepository, IMapper mapper, IIdentityService identityService, IMediator mediator, IUnitOfWork<ICommunityDbContext> unitOfWork, ILogger<MealCommandHandler> logger)
            : base(identityService, mediator, unitOfWork, logger)
        {
            _mealRepository = mealRepository;
            _foodRepository = foodRepository;
            _mapper = mapper;
            _currentProfileId = GetCurrentProfileId();
        }

        public async Task<CommandResult> Handle(UpdateMealCommand request, CancellationToken cancellationToken)
        {
            Meal meal = await _mealRepository.GetByIdAsync(request.MealId);
            if (!FoundValidMeal(meal))
                return FailureDueToMealNotFound();

            meal.Update(
                request.Name,
                request.Description,
                _mapper.Map<Time>(request.TimeOfDay)
                );

            await _mealRepository.UpdateAsync(meal);

            return await CommitAndPublishDefaultAsync();
        }

        public async Task<CommandResult> Handle(AddMealFoodCommand request, CancellationToken cancellationToken)
        {
            Meal meal = await _mealRepository.GetByIdAsync(request.MealId);
            if (!FoundValidMeal(meal))
                return FailureDueToMealNotFound();

            Food food = await _foodRepository.GetByIdAndProfileIdAsync(request.FoodId, _currentProfileId);
            if (food == null)
                return FailureDueToCustomFoodNotFound();

            MealFood mealFood = new MealFood(
                food,
                request.Quantity
                );

            meal.AddMealFood(mealFood);
            if (!meal.IsValid)
                return FailureDueToEntityStateInconsistency(meal);

            await _mealRepository.UpdateAsync(meal);

            return await CommitAndPublishAsync(new MealFoodAddedDomainEvent(
                meal.Id,
                meal.DietId,
                mealFood.FoodId
                ));
        }

        public async Task<CommandResult> Handle(RemoveMealFoodCommand request, CancellationToken cancellationToken)
        {
            Meal meal = await _mealRepository.GetByIdAsync(request.MealId);
            if (!FoundValidMeal(meal))
                return FailureDueToMealNotFound();

            MealFood mealFood = meal.FindMealFood(request.MealFoodId);

            meal.RemoveMealFood(mealFood);
            if (!mealFood.IsValid)
                return FailureDueToEntityStateInconsistency(meal);

            await _mealRepository.UpdateAsync(meal);

            return await CommitAndPublishAsync(new MealFoodRemovedDomainEvent(
                meal.Id,
                meal.DietId,
                mealFood.FoodId
                ));
        }

        private bool FoundValidMeal(Meal meal)
        {
            return meal != null && meal.ProfileId == _currentProfileId;
        }

        private CommandResult FailureDueToMealNotFound()
        {
            return FailureDueToEntityNotFound("Refeição não encontrada", "Nenhuma refeição foi encontrada no banco de dados.");
        }
    }
}
